namespace OrderProcessing.Infrastructure.Options;

public sealed class EventHubOptions
{
    public string EventHubConnection { get; set; } = string.Empty;
    public string EventHubName { get; set; } = string.Empty;
}
