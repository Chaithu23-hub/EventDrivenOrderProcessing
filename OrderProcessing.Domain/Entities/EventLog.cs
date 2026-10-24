namespace OrderProcessing.Domain.Entities;

public enum EventStatus
{
    Success,
    Failed
}

public sealed class EventLog
{
    public required string Id { get; init; }
    public required string OrderId { get; init; }
    public required string EventType { get; init; }
    public required string Payload { get; init; }
    public required EventStatus Status { get; set; }
    public int RetryCount { get; set; }
    public string? ErrorMessage { get; set; }
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset LastAttemptAt { get; set; }
    public DateTimeOffset? ProcessedAt { get; set; }
    public required string CorrelationId { get; init; }
}
