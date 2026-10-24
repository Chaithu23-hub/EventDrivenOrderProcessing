using OrderProcessing.Domain.Entities;

namespace OrderProcessing.Application.DTOs;

public sealed record OrderEventPayload(
    string EventId,
    string OrderId,
    string Sku,
    int Quantity,
    OrderEventType EventType,
    string CorrelationId,
    DateTimeOffset CreatedAt)
{
    public static OrderEventPayload From(OrderEvent orderEvent) =>
        new(orderEvent.EventId, orderEvent.OrderId, orderEvent.Sku, orderEvent.Quantity,
            orderEvent.EventType, orderEvent.CorrelationId, orderEvent.CreatedAt);

    public OrderEvent ToOrderEvent() =>
        new(EventId, OrderId, Sku, Quantity, EventType, CorrelationId, CreatedAt);
}
