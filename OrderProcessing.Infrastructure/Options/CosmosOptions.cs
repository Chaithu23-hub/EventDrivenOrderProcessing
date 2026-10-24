namespace OrderProcessing.Infrastructure.Options;

public sealed class CosmosOptions
{
    public string CosmosConnection { get; set; } = string.Empty;
    public string DatabaseName { get; set; } = "EventLogDb";
    public string ContainerName { get; set; } = "EventLogs";
}
