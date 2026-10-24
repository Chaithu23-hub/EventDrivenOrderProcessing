using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using OrderProcessing.Application;
using OrderProcessing.Infrastructure;

var host = new HostBuilder()
    .ConfigureAppConfiguration((_, configuration) =>
    {
        configuration.AddJsonFile("appsettings.json", optional: true, reloadOnChange: true);
        configuration.AddEnvironmentVariables();
    })
    .ConfigureFunctionsWorkerDefaults()
    .ConfigureServices((context, services) =>
    {
        services.AddApplication();
        services.AddFunctionInfrastructure(context.Configuration);
        services.AddOptions<FunctionSettings>()
            .Configure<IConfiguration>((settings, configuration) => configuration.Bind(settings))
            .Validate(settings => !string.IsNullOrWhiteSpace(settings.EventHubName), "EventHubName is required.")
            .Validate(settings => !string.IsNullOrWhiteSpace(settings.ConsumerGroup), "ConsumerGroup is required.")
            .Validate(settings => !string.IsNullOrWhiteSpace(settings.RetrySchedule), "RetrySchedule is required.")
            .ValidateOnStart();
    })
    .Build();

await host.RunAsync();

public sealed class FunctionSettings
{
    public string EventHubName { get; set; } = string.Empty;
    public string ConsumerGroup { get; set; } = string.Empty;
    public string RetrySchedule { get; set; } = string.Empty;
}
