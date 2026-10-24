namespace OrderProcessing.Domain.Entities;

public enum OrderEventType
{
    OrderCreated,
    OrderCancelled
}

public sealed record OrderEvent(
    string EventId,
    string OrderId,
    string Sku,
    int Quantity,
    OrderEventType EventType,
    string CorrelationId,
    DateTimeOffset CreatedAt);
