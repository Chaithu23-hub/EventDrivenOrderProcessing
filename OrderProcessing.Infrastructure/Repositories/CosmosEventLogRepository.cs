using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Options;
using OrderProcessing.Application.Abstractions;
using OrderProcessing.Domain.Entities;
using OrderProcessing.Infrastructure.Options;

namespace OrderProcessing.Infrastructure.Repositories;

public sealed class CosmosEventLogRepository : IEventLogRepository
{
    private readonly Container _container;

    public CosmosEventLogRepository(CosmosClient cosmosClient, IOptions<CosmosOptions> options)
    {
        _container = cosmosClient.GetContainer(options.Value.DatabaseName, options.Value.ContainerName);
    }

    public async Task UpsertAsync(EventLog eventLog, CancellationToken cancellationToken = default)
    {
        await _container.UpsertItemAsync(
            EventLogDocument.FromDomain(eventLog),
            new PartitionKey(eventLog.OrderId),
            cancellationToken: cancellationToken);
    }

    public Task<IReadOnlyList<EventLog>> GetFailedAsync(
        int maxRetryCount,
        int batchSize,
        CancellationToken cancellationToken = default) =>
        QueryAsync(
            "SELECT TOP @batchSize * FROM c WHERE c.status = @status AND c.retryCount < @maxRetryCount ORDER BY c.createdAt ASC",
            maxRetryCount,
            batchSize,
            cancellationToken);

    public Task<IReadOnlyList<EventLog>> GetExhaustedFailedAsync(
        int maxRetryCount,
        int batchSize,
        CancellationToken cancellationToken = default) =>
        QueryAsync(
            "SELECT TOP @batchSize * FROM c WHERE c.status = @status AND c.retryCount >= @maxRetryCount ORDER BY c.createdAt ASC",
            maxRetryCount,
            batchSize,
            cancellationToken);

    private async Task<IReadOnlyList<EventLog>> QueryAsync(
        string query,
        int maxRetryCount,
        int batchSize,
        CancellationToken cancellationToken)
    {
        var definition = new QueryDefinition(query)
            .WithParameter("@batchSize", batchSize)
            .WithParameter("@status", EventStatus.Failed.ToString())
            .WithParameter("@maxRetryCount", maxRetryCount);
        using var iterator = _container.GetItemQueryIterator<EventLogDocument>(
            definition,
            requestOptions: new QueryRequestOptions { MaxItemCount = batchSize });
        var results = new List<EventLog>(batchSize);
        while (iterator.HasMoreResults && results.Count < batchSize)
        {
            var page = await iterator.ReadNextAsync(cancellationToken);
            results.AddRange(page.Select(document => document.ToDomain()));
        }

        return results.Take(batchSize).ToArray();
    }

    private sealed class EventLogDocument
    {
        public required string Id { get; init; }
        public required string OrderId { get; init; }
        public required string EventType { get; init; }
        public required string Payload { get; init; }
        public required string Status { get; init; }
        public int RetryCount { get; init; }
        public string? ErrorMessage { get; init; }
        public DateTimeOffset CreatedAt { get; init; }
        public DateTimeOffset LastAttemptAt { get; init; }
        public DateTimeOffset? ProcessedAt { get; init; }
        public required string CorrelationId { get; init; }

        public static EventLogDocument FromDomain(EventLog eventLog) =>
            new()
            {
                Id = eventLog.Id,
                OrderId = eventLog.OrderId,
                EventType = eventLog.EventType,
                Payload = eventLog.Payload,
                Status = eventLog.Status.ToString(),
                RetryCount = eventLog.RetryCount,
                ErrorMessage = eventLog.ErrorMessage,
                CreatedAt = eventLog.CreatedAt,
                LastAttemptAt = eventLog.LastAttemptAt,
                ProcessedAt = eventLog.ProcessedAt,
                CorrelationId = eventLog.CorrelationId
            };

        public EventLog ToDomain() =>
            new()
            {
                Id = Id,
                OrderId = OrderId,
                EventType = EventType,
                Payload = Payload,
                Status = Enum.Parse<EventStatus>(Status, ignoreCase: false),
                RetryCount = RetryCount,
                ErrorMessage = ErrorMessage,
                CreatedAt = CreatedAt,
                LastAttemptAt = LastAttemptAt,
                ProcessedAt = ProcessedAt,
                CorrelationId = CorrelationId
            };
    }
}
