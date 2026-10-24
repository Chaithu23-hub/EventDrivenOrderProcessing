using System.Text.Json;
using System.Text.Json.Serialization;
using Azure.Messaging.EventHubs;
using Azure.Messaging.EventHubs.Producer;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OrderProcessing.Application.Abstractions;
using OrderProcessing.Application.DTOs;
using OrderProcessing.Domain.Entities;
using OrderProcessing.Infrastructure.Options;

namespace OrderProcessing.Infrastructure.Publishers;

public sealed class EventHubOrderEventPublisher : IOrderEventPublisher, IAsyncDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly EventHubProducerClient _producer;
    private readonly ILogger<EventHubOrderEventPublisher> _logger;

    public EventHubOrderEventPublisher(
        IOptions<EventHubOptions> options,
        ILogger<EventHubOrderEventPublisher> logger)
    {
        var settings = options.Value;
        _producer = new EventHubProducerClient(settings.EventHubConnection, settings.EventHubName);
        _logger = logger;
    }

    public async Task PublishAsync(OrderEvent orderEvent, CancellationToken cancellationToken = default)
    {
        var message = JsonSerializer.Serialize(OrderEventPayload.From(orderEvent), JsonOptions);
        var eventData = new EventData(BinaryData.FromString(message))
        {
            CorrelationId = orderEvent.CorrelationId,
            ContentType = "application/json"
        };
        eventData.Properties["eventType"] = orderEvent.EventType.ToString();
        eventData.Properties["orderId"] = orderEvent.OrderId;

        using var batch = await _producer.CreateBatchAsync(cancellationToken);
        if (!batch.TryAdd(eventData))
            throw new InvalidOperationException($"Event {orderEvent.EventId} exceeds the Event Hubs message size limit.");

        await _producer.SendAsync(batch, cancellationToken);
        _logger.LogInformation(
            "Published eventId {EventId} for orderId {OrderId}, correlationId {CorrelationId}.",
            orderEvent.EventId, orderEvent.OrderId, orderEvent.CorrelationId);
    }

    public ValueTask DisposeAsync() => _producer.DisposeAsync();
}
