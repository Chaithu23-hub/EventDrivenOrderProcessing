using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using OrderProcessing.Application.Abstractions;
using OrderProcessing.Application.DTOs;
using OrderProcessing.Domain.Entities;
using OrderProcessing.Domain.Exceptions;
using Polly;
using Polly.Retry;

namespace OrderProcessing.Application.UseCases;

public sealed class ProcessOrderEventUseCase
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly IEventLogRepository _eventLogRepository;
    private readonly IInventoryService _inventoryService;
    private readonly ILogger<ProcessOrderEventUseCase> _logger;
    private readonly ResiliencePipeline _pipeline;

    public ProcessOrderEventUseCase(
        IEventLogRepository eventLogRepository,
        IInventoryService inventoryService,
        ILogger<ProcessOrderEventUseCase> logger)
    {
        _eventLogRepository = eventLogRepository;
        _inventoryService = inventoryService;
        _logger = logger;
        _pipeline = new ResiliencePipelineBuilder()
            .AddRetry(new RetryStrategyOptions
            {
                MaxRetryAttempts = 2,
                Delay = TimeSpan.FromMilliseconds(100),
                BackoffType = DelayBackoffType.Exponential,
                ShouldHandle = new PredicateBuilder().Handle<TransientProcessingException>(),
                OnRetry = args =>
                {
                    _logger.LogWarning(
                        "Transient processing failure for eventId {EventId}, orderId {OrderId}, correlationId {CorrelationId}; retry {RetryAttempt}.",
                        args.Context.Properties.GetValue(EventContextKeys.EventId, string.Empty),
                        args.Context.Properties.GetValue(EventContextKeys.OrderId, string.Empty),
                        args.Context.Properties.GetValue(EventContextKeys.CorrelationId, string.Empty),
                        args.AttemptNumber);
                    return ValueTask.CompletedTask;
                }
            })
            .Build();
    }

    public async Task<ProcessOrderEventResult> ExecuteAsync(
        OrderEvent orderEvent,
        int retryCount = 0,
        DateTimeOffset? originalCreatedAt = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(orderEvent);
        var context = ResilienceContextPool.Shared.Get(cancellationToken);
        context.Properties.Set(EventContextKeys.EventId, orderEvent.EventId);
        context.Properties.Set(EventContextKeys.OrderId, orderEvent.OrderId);
        context.Properties.Set(EventContextKeys.CorrelationId, orderEvent.CorrelationId);

        try
        {
            Validate(orderEvent);
            await _pipeline.ExecuteAsync(
                async resilienceContext =>
                    await _inventoryService.ApplyAsync(orderEvent, resilienceContext.CancellationToken),
                context);

            var processedAt = DateTimeOffset.UtcNow;
            var successLog = CreateLog(orderEvent, EventStatus.Success, retryCount, null,
                processedAt, originalCreatedAt, processedAt);
            await _eventLogRepository.UpsertAsync(successLog, cancellationToken);
            _logger.LogInformation(
                "Processed eventId {EventId} for orderId {OrderId}, correlationId {CorrelationId}.",
                orderEvent.EventId, orderEvent.OrderId, orderEvent.CorrelationId);
            return new ProcessOrderEventResult(true);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            var failedAt = DateTimeOffset.UtcNow;
            _logger.LogError(ex,
                "Failed eventId {EventId} for orderId {OrderId}, correlationId {CorrelationId}.",
                orderEvent.EventId, orderEvent.OrderId, orderEvent.CorrelationId);
            var failedLog = CreateLog(orderEvent, EventStatus.Failed, retryCount, ex.Message,
                failedAt, originalCreatedAt, null);
            await _eventLogRepository.UpsertAsync(failedLog, cancellationToken);
            return new ProcessOrderEventResult(false, ex.Message);
        }
        finally
        {
            ResilienceContextPool.Shared.Return(context);
        }
    }

    private static void Validate(OrderEvent orderEvent)
    {
        if (string.IsNullOrWhiteSpace(orderEvent.EventId) ||
            string.IsNullOrWhiteSpace(orderEvent.OrderId) ||
            string.IsNullOrWhiteSpace(orderEvent.Sku) ||
            string.IsNullOrWhiteSpace(orderEvent.CorrelationId) ||
            orderEvent.Quantity <= 0 ||
            !Enum.IsDefined(orderEvent.EventType))
            throw new InvalidOrderEventException("Order event fields are invalid.");
    }

    private static EventLog CreateLog(
        OrderEvent orderEvent,
        EventStatus status,
        int retryCount,
        string? errorMessage,
        DateTimeOffset lastAttemptAt,
        DateTimeOffset? createdAt,
        DateTimeOffset? processedAt) =>
        new()
        {
            Id = orderEvent.EventId,
            OrderId = orderEvent.OrderId,
            EventType = orderEvent.EventType.ToString(),
            Payload = JsonSerializer.Serialize(OrderEventPayload.From(orderEvent), JsonOptions),
            Status = status,
            RetryCount = retryCount,
            ErrorMessage = errorMessage,
            CreatedAt = createdAt ?? orderEvent.CreatedAt,
            LastAttemptAt = lastAttemptAt,
            ProcessedAt = processedAt,
            CorrelationId = orderEvent.CorrelationId
        };

    private static class EventContextKeys
    {
        public static readonly ResiliencePropertyKey<string> EventId = new("eventId");
        public static readonly ResiliencePropertyKey<string> OrderId = new("orderId");
        public static readonly ResiliencePropertyKey<string> CorrelationId = new("correlationId");
    }
}
