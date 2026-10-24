using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OrderProcessing.Application.Abstractions;
using OrderProcessing.Application.DTOs;
using OrderProcessing.Application.Options;
using OrderProcessing.Domain.Entities;

namespace OrderProcessing.Application.UseCases;

public sealed class RetryFailedEventsUseCase(
    IEventLogRepository eventLogRepository,
    ProcessOrderEventUseCase processOrderEvent,
    IOptions<RetryOptions> options,
    ILogger<RetryFailedEventsUseCase> logger)
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public async Task ExecuteAsync(CancellationToken cancellationToken = default)
    {
        var retryOptions = options.Value;
        var exhausted = await eventLogRepository.GetExhaustedFailedAsync(
            retryOptions.MaxRetryCount, retryOptions.BatchSize, cancellationToken);
        foreach (var item in exhausted)
        {
            logger.LogWarning(
                "Event reached MaxRetryCount {MaxRetryCount}; it will not be retried. eventId {EventId}, orderId {OrderId}, correlationId {CorrelationId}.",
                retryOptions.MaxRetryCount, item.Id, item.OrderId, item.CorrelationId);
        }

        var failedEvents = await eventLogRepository.GetFailedAsync(
            retryOptions.MaxRetryCount, retryOptions.BatchSize, cancellationToken);
        foreach (var eventLog in failedEvents)
        {
            try
            {
                var payload = JsonSerializer.Deserialize<OrderEventPayload>(eventLog.Payload, JsonOptions)
                    ?? throw new JsonException("Event payload was empty.");
                var result = await processOrderEvent.ExecuteAsync(
                    payload.ToOrderEvent(), eventLog.RetryCount, eventLog.CreatedAt, cancellationToken);

                if (!result.Succeeded)
                {
                    eventLog.Status = EventStatus.Failed;
                    eventLog.RetryCount++;
                    eventLog.ErrorMessage = result.ErrorMessage;
                    eventLog.LastAttemptAt = DateTimeOffset.UtcNow;
                    eventLog.ProcessedAt = null;
                    await eventLogRepository.UpsertAsync(eventLog, cancellationToken);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
            {
                logger.LogError(ex,
                    "Retry failed for eventId {EventId}, orderId {OrderId}, correlationId {CorrelationId}.",
                    eventLog.Id, eventLog.OrderId, eventLog.CorrelationId);
                eventLog.Status = EventStatus.Failed;
                eventLog.RetryCount++;
                eventLog.ErrorMessage = ex.Message;
                eventLog.LastAttemptAt = DateTimeOffset.UtcNow;
                eventLog.ProcessedAt = null;
                await eventLogRepository.UpsertAsync(eventLog, cancellationToken);
            }
        }
    }
}
