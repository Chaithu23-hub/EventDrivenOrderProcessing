using OrderProcessing.Domain.Entities;

namespace OrderProcessing.Application.Abstractions;

public interface IEventLogRepository
{
    Task UpsertAsync(EventLog eventLog, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<EventLog>> GetFailedAsync(int maxRetryCount, int batchSize, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<EventLog>> GetExhaustedFailedAsync(int maxRetryCount, int batchSize, CancellationToken cancellationToken = default);
}
