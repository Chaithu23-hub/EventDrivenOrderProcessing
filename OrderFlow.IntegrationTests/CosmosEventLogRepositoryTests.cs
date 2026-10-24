using System.Text.Json;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Options;
using OrderProcessing.Domain.Entities;
using OrderProcessing.Infrastructure.Options;
using OrderProcessing.Infrastructure.Repositories;

namespace OrderFlow.IntegrationTests;

public sealed class CosmosEventLogRepositoryTests
{
    [CosmosFact]
    public async Task UpsertAndFailedQuery_UseEventIdAndOrderPartitionKey()
    {
        var connectionString = Environment.GetEnvironmentVariable("COSMOS_CONNECTION_STRING");
        Assert.False(string.IsNullOrWhiteSpace(connectionString));

        const string databaseName = "EventLogDb";
        const string containerName = "EventLogs";
        using var client = new CosmosClient(connectionString, new CosmosClientOptions
        {
            SerializerOptions = new CosmosSerializationOptions
            {
                PropertyNamingPolicy = CosmosPropertyNamingPolicy.CamelCase
            }
        });
        var database = (await client.CreateDatabaseIfNotExistsAsync(databaseName)).Database;
        var container = (await database.CreateContainerIfNotExistsAsync(containerName, "/orderId")).Container;
        var repository = new CosmosEventLogRepository(client, Options.Create(new CosmosOptions
        {
            CosmosConnection = connectionString,
            DatabaseName = databaseName,
            ContainerName = containerName
        }));
        var eventId = Guid.NewGuid().ToString("N");
        var exhaustedEventId = Guid.NewGuid().ToString("N");
        var orderId = Guid.NewGuid().ToString("N");
        var eventInserted = false;
        var exhaustedEventInserted = false;
        var eventLog = new EventLog
        {
            Id = eventId,
            OrderId = orderId,
            EventType = "OrderCreated",
            Payload = "{}",
            Status = EventStatus.Failed,
            RetryCount = 0,
            ErrorMessage = "test failure",
            CreatedAt = DateTimeOffset.UtcNow,
            LastAttemptAt = DateTimeOffset.UtcNow,
            CorrelationId = Guid.NewGuid().ToString("N")
        };
        var exhaustedLog = new EventLog
        {
            Id = exhaustedEventId,
            OrderId = orderId,
            EventType = "OrderCreated",
            Payload = "{}",
            Status = EventStatus.Failed,
            RetryCount = 3,
            ErrorMessage = "retry limit reached",
            CreatedAt = DateTimeOffset.UtcNow,
            LastAttemptAt = DateTimeOffset.UtcNow,
            CorrelationId = Guid.NewGuid().ToString("N")
        };

        try
        {
            await repository.UpsertAsync(eventLog);
            eventInserted = true;
            await repository.UpsertAsync(exhaustedLog);
            exhaustedEventInserted = true;
            var eligible = await repository.GetFailedAsync(maxRetryCount: 3, batchSize: 10);
            Assert.Contains(eligible,
                item => item.Id == eventId && item.OrderId == orderId);
            Assert.DoesNotContain(eligible, item => item.Id == exhaustedEventId);
            Assert.Contains(await repository.GetExhaustedFailedAsync(maxRetryCount: 3, batchSize: 10),
                item => item.Id == exhaustedEventId);

            eventLog.Status = EventStatus.Success;
            eventLog.ErrorMessage = null;
            eventLog.ProcessedAt = DateTimeOffset.UtcNow;
            await repository.UpsertAsync(eventLog);

            var remaining = await repository.GetFailedAsync(maxRetryCount: 3, batchSize: 10);
            Assert.DoesNotContain(remaining,
                item => item.Id == eventId);
            using var response = await container.ReadItemStreamAsync(eventId, new PartitionKey(orderId));
            using var document = await JsonDocument.ParseAsync(response.Content);
            Assert.Equal(eventId, document.RootElement.GetProperty("id").GetString());
            Assert.Equal("Success", document.RootElement.GetProperty("status").GetString());
            Assert.Equal(orderId, document.RootElement.GetProperty("orderId").GetString());
            Assert.Equal("OrderCreated", document.RootElement.GetProperty("eventType").GetString());
            Assert.Equal(0, document.RootElement.GetProperty("retryCount").GetInt32());
            Assert.True(document.RootElement.TryGetProperty("payload", out _));
            Assert.True(document.RootElement.TryGetProperty("createdAt", out _));
            Assert.True(document.RootElement.TryGetProperty("lastAttemptAt", out _));
            Assert.True(document.RootElement.TryGetProperty("processedAt", out _));
            Assert.True(document.RootElement.TryGetProperty("correlationId", out _));
            Assert.Equal(JsonValueKind.Null, document.RootElement.GetProperty("errorMessage").ValueKind);
        }
        finally
        {
            if (eventInserted)
                await container.DeleteItemStreamAsync(eventId, new PartitionKey(orderId));
            if (exhaustedEventInserted)
                await container.DeleteItemStreamAsync(exhaustedEventId, new PartitionKey(orderId));
        }
    }

    private sealed class CosmosFactAttribute : FactAttribute
    {
        public CosmosFactAttribute()
        {
            if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("COSMOS_CONNECTION_STRING")))
                Skip = "Set COSMOS_CONNECTION_STRING to a Cosmos DB or Cosmos DB Emulator endpoint to run this integration test.";
        }
    }
}
