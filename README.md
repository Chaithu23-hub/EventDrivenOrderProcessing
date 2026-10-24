# Order Processing: Event Hubs, Functions, and Cosmos DB

A .NET 8 isolated-worker sample using Clean Architecture. The Order API publishes `OrderCreated` events to Event Hubs. The Functions app consumes event batches, applies inventory changes, and upserts each outcome to Cosmos DB. An hourly timer retries eligible failures.

## Project tree

```text
OrderProcessing.sln
├── OrderProcessing.Domain/
│   ├── Entities/                  OrderEvent, EventLog, InventoryItem, EventStatus
│   └── Exceptions/                Domain and transient processing exceptions
├── OrderProcessing.Application/
│   ├── Abstractions/              Repository, inventory, and publisher interfaces
│   ├── DTOs/                      Event payload and processing result
│   ├── Options/                   RetryOptions
│   └── UseCases/                  ProcessOrderEventUseCase, RetryFailedEventsUseCase
├── OrderProcessing.Infrastructure/
│   ├── Options/                   Azure and in-memory inventory settings
│   ├── Publishers/                EventHubOrderEventPublisher
│   ├── Repositories/              CosmosEventLogRepository
│   └── Services/                  InMemoryInventoryService
├── Order.Api/                     POST /api/orders
├── Inventory.Functions/            Event Hub and timer triggers (isolated worker)
├── Application.UnitTests/          xUnit + Moq use-case tests
└── OrderFlow.IntegrationTests/     Cosmos DB repository integration test
```

Dependencies point inward: Infrastructure references Application and Domain; Application references Domain; Domain has no package references. The API only registers the publisher, while Functions registers Cosmos and inventory services.

## Cosmos DB document

The sample expects database `EventLogDb`, container `EventLogs`, partition key `/orderId`. Each document has `id` (the event ID), `orderId`, `eventType`, `payload`, string `status` (`Success` or `Failed`), `retryCount`, `errorMessage`, `createdAt`, `lastAttemptAt`, `processedAt`, and `correlationId`. Writes use `UpsertItemAsync` with the order ID partition key. The retry query filters `status = 'Failed' AND retryCount < MaxRetryCount`; exhausted failures are queried separately only to emit warnings.

The in-memory inventory is deliberately a local demo implementation. It serializes access and remembers processed event IDs for the lifetime of the Functions process, so Event Hubs redelivery/retry cannot deduct twice while that process is running. For scale-out or restart-safe production idempotency, replace it with a durable inventory store and an atomic event-ID/idempotency record.

## Prerequisites

- .NET 8 SDK
- Azure Functions Core Tools v4
- Azurite (for local Functions host storage)
- Azure CLI (`az`)
- An Azure Event Hubs namespace/hub and Cosmos DB account, or compatible local emulators

The checked-in `appsettings.json` and `local.settings.json` contain placeholders only. Supply real values using local settings or environment variables; never commit credentials.

## Provision Azure resources

Run in PowerShell, choosing a globally unique lowercase storage/account/function name and a supported region. The commands below create a resource group, a Standard Event Hubs namespace/hub, a Cosmos DB SQL API account/database/container, storage for Functions, and a Linux Consumption Function App.

```powershell
$RG = "rg-order-processing-demo"
$LOCATION = "eastus"
$EH_NAMESPACE = "eh-order-demo-<unique>"
$EH_NAME = "order-events"
$COSMOS = "cosmos-order-demo-<unique>"
$STORAGE = "storderdemo<unique>"
$FUNCTION_APP = "func-order-demo-<unique>"

az group create --name $RG --location $LOCATION

az eventhubs namespace create --resource-group $RG --name $EH_NAMESPACE --location $LOCATION --sku Standard
az eventhubs eventhub create --resource-group $RG --namespace-name $EH_NAMESPACE --name $EH_NAME --partition-count 2 --message-retention 1

az cosmosdb create --resource-group $RG --name $COSMOS --locations regionName=$LOCATION failoverPriority=0 isZoneRedundant=False --kind GlobalDocumentDB --default-consistency-level Session
az cosmosdb sql database create --resource-group $RG --account-name $COSMOS --name EventLogDb
az cosmosdb sql container create --resource-group $RG --account-name $COSMOS --database-name EventLogDb --name EventLogs --partition-key-path "/orderId" --throughput 400

az storage account create --resource-group $RG --name $STORAGE --location $LOCATION --sku Standard_LRS
az functionapp create --resource-group $RG --name $FUNCTION_APP --storage-account $STORAGE --consumption-plan-location $LOCATION --runtime dotnet-isolated --runtime-version 8 --functions-version 4 --os-type Linux
```

Obtain the Event Hubs namespace connection string and Cosmos connection string without putting either in source:

```powershell
$EH_CONNECTION = az eventhubs namespace authorization-rule keys list --resource-group $RG --namespace-name $EH_NAMESPACE --name RootManageSharedAccessKey --query primaryConnectionString --output tsv
$COSMOS_CONNECTION = az cosmosdb keys list --resource-group $RG --name $COSMOS --type connection-strings --query "connectionStrings[0].connectionString" --output tsv
$STORAGE_CONNECTION = az storage account show-connection-string --resource-group $RG --name $STORAGE --query connectionString --output tsv
```

Set Function App settings. The API uses the same Event Hubs values via environment variables when run locally.

```powershell
az functionapp config appsettings set --resource-group $RG --name $FUNCTION_APP --settings `
  "AzureWebJobsStorage=$STORAGE_CONNECTION" `
  "FUNCTIONS_WORKER_RUNTIME=dotnet-isolated" `
  "EventHubConnection=$EH_CONNECTION" `
  "EventHubName=$EH_NAME" `
  "ConsumerGroup=`$Default" `
  "CosmosConnection=$COSMOS_CONNECTION" `
  "DatabaseName=EventLogDb" `
  "ContainerName=EventLogs" `
  "RetrySchedule=0 0 * * * *" `
  "MaxRetryCount=5" `
  "BatchSize=100" `
  "InventoryFailingSku=FAIL-001"

Push-Location Inventory.Functions
func azure functionapp publish $FUNCTION_APP
Pop-Location
```

For production, assign managed identities and least-privilege data-plane roles instead of using connection strings.

## Run locally

1. Start Azurite (the Functions host uses `UseDevelopmentStorage=true`) and either use Azure resources or a Cosmos DB Emulator.
2. Replace placeholders in `Inventory.Functions/local.settings.json`; set the Event Hubs namespace connection string (not an event-hub-scoped string) and your hub name. Set `CosmosConnection` to the account/emulator connection string. `ConsumerGroup` can be `$Default`.
3. `InventoryFailingSku` defaults to `FAIL-001`, causing that SKU to simulate a transient inventory outage. Set it to an empty string when ready to demonstrate retry success.
4. In one PowerShell terminal:

   ```powershell
   dotnet run --project Order.Api
   ```

   The API listens at the URL printed by `dotnet run` (the included launch profile uses `http://localhost:5123`).
5. In another PowerShell terminal:

   ```powershell
   Set-Location Inventory.Functions
   func start --port 7071
   ```

   The timer schedule is `0 0 * * * *` (top of every hour); it does not run on startup.

## Test

Run the unit suite:

```powershell
dotnet test Application.UnitTests\Application.UnitTests.csproj
```

It covers process success, process failure after three Polly attempts, retry success, retry failure with `retryCount` increment, and an exhausted event not being processed.

The Cosmos integration test creates the database/container if missing and verifies document fields, failed-event selection, and upsert-by-event-ID behavior. Run it against a Cosmos DB account or emulator:

```powershell
$env:COSMOS_CONNECTION_STRING = "<COSMOS_CONNECTION_STRING>"
dotnet test OrderFlow.IntegrationTests\OrderFlow.IntegrationTests.csproj
```

Without `COSMOS_CONNECTION_STRING`, xUnit reports the integration test as skipped. The test writes a unique event and removes it when complete.

### End-to-end Order API → Event Hubs → Function → Cosmos DB

1. With both local apps running, send a valid order:

   ```powershell
   $created = Invoke-RestMethod -Method Post -Uri http://localhost:5123/api/orders `
     -ContentType "application/json" -Body '{"sku":"SKU-OK-01","quantity":2}'
   $created
   ```

   Check the Cosmos `EventLogs` container for `id == $created.eventId`. It should have `"status": "Success"`, `retryCount: 0`, and `processedAt` set.
2. Send a simulated failing order:

   ```powershell
   $failed = Invoke-RestMethod -Method Post -Uri http://localhost:5123/api/orders `
     -ContentType "application/json" -Body '{"sku":"FAIL-001","quantity":1}'
   $failed
   ```

   Wait for Event Hubs processing, then verify its Cosmos document has `"status": "Failed"`, `retryCount: 0`, and a populated `errorMessage`. The Function makes three in-process attempts before logging this failure.
3. Change `InventoryFailingSku` to an empty string in `Inventory.Functions/local.settings.json` and restart `func start`. Invoke the timer function manually:

   ```powershell
   Invoke-RestMethod -Method Post -Uri http://localhost:7071/admin/functions/RetryFailedEvents `
     -ContentType "application/json" -Body '{}'
   ```

   The retried event should be upserted to `"status": "Success"` with `processedAt` set.
4. To verify the retry ceiling, set `InventoryFailingSku` back to `FAIL-001`, restart Functions, and submit another `FAIL-001` order. After its initial failure, invoke the admin endpoint five times (or the configured `MaxRetryCount`) and verify `retryCount` reaches the maximum. Invoke it once more: the document's `retryCount` and `lastAttemptAt` must remain unchanged, and the Functions logs should contain the exhausted-retry warning.

The admin endpoint is provided by the local Functions host for development. It is not an application endpoint and should not be exposed publicly.

## Configuration keys

`EventHubConnection`, `EventHubName`, `ConsumerGroup`, `CosmosConnection`, `RetrySchedule`, `MaxRetryCount`, and `BatchSize` are configurable through app settings/environment variables. Cosmos defaults to database `EventLogDb` and container `EventLogs`. The timer binding reads the NCRONTAB schedule from `RetrySchedule`.

## Operational notes

- Event Hubs batches are processed one message at a time. Application failures are logged and persisted without throwing from the trigger, allowing checkpoint advancement and continued processing.
- Polly retries only `TransientProcessingException` twice after the initial attempt, with exponential backoff (100 ms base delay). Infrastructure adapters should translate provider-specific transient errors to this application/domain exception.
- Retry failure increments the persisted count once per scheduled/manual retry execution. A successful retry sets `Success` and `processedAt`; exhausted records are excluded from the retry query.
- Order cancellations are supported by the application inventory service and event payload contract; this sample's HTTP endpoint intentionally only creates orders.
