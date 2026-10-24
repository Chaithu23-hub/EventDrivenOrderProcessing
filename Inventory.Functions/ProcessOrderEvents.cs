using System.Text.Json;
using System.Text.Json.Serialization;
using Azure.Messaging.EventHubs;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using OrderProcessing.Application.DTOs;
using OrderProcessing.Application.UseCases;

namespace Inventory.Functions;

public sealed class ProcessOrderEvents(
    ProcessOrderEventUseCase processOrderEvent,
    ILogger<ProcessOrderEvents> logger)
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() }
    };

    [Function(nameof(ProcessOrderEvents))]
    public async Task Run(
        [EventHubTrigger("%EventHubName%", Connection = "EventHubConnection", ConsumerGroup = "%ConsumerGroup%")]
        EventData[] batch,
        CancellationToken cancellationToken)
    {
        foreach (var eventData in batch)
        {
            string? eventId = null;
            string? orderId = null;
            var correlationId = eventData.CorrelationId;
            try
            {
                var payload = JsonSerializer.Deserialize<OrderEventPayload>(eventData.EventBody.ToArray(), JsonOptions)
                    ?? throw new JsonException("Event payload was empty.");
                eventId = payload.EventId;
                orderId = payload.OrderId;
                correlationId = payload.CorrelationId;
                await processOrderEvent.ExecuteAsync(payload.ToOrderEvent(), cancellationToken: cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
            {
                logger.LogError(ex,
                    "Unable to handle an Event Hubs message. eventId {EventId}, orderId {OrderId}, correlationId {CorrelationId}.",
                    eventId, orderId, correlationId);
            }
        }
    }
}
