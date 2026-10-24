namespace OrderProcessing.Application.Options;

public sealed class RetryOptions
{
    public int MaxRetryCount { get; set; } = 5;
    public int BatchSize { get; set; } = 100;
}
