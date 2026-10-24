using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using OrderProcessing.Application.Abstractions;
using OrderProcessing.Infrastructure.Options;
using OrderProcessing.Infrastructure.Publishers;
using OrderProcessing.Infrastructure.Repositories;
using OrderProcessing.Infrastructure.Services;

namespace OrderProcessing.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddOrderEventPublishing(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddOptions<EventHubOptions>()
            .Bind(configuration)
            .Validate(options => !string.IsNullOrWhiteSpace(options.EventHubConnection), "EventHubConnection is required.")
            .Validate(options => !string.IsNullOrWhiteSpace(options.EventHubName), "EventHubName is required.")
            .ValidateOnStart();
        services.AddSingleton<IOrderEventPublisher, EventHubOrderEventPublisher>();
        return services;
    }

    public static IServiceCollection AddFunctionInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddOptions<CosmosOptions>()
            .Bind(configuration)
            .Validate(options => !string.IsNullOrWhiteSpace(options.CosmosConnection), "CosmosConnection is required.")
            .ValidateOnStart();
        services.AddOptions<InventoryOptions>().Bind(configuration);

        services.AddSingleton(provider =>
        {
            var options = provider.GetRequiredService<IOptions<CosmosOptions>>().Value;
            return new CosmosClient(options.CosmosConnection, new CosmosClientOptions
            {
                SerializerOptions = new CosmosSerializationOptions
                {
                    PropertyNamingPolicy = CosmosPropertyNamingPolicy.CamelCase
                }
            });
        });
        services.AddSingleton<IEventLogRepository, CosmosEventLogRepository>();
        services.AddSingleton<IInventoryService, InMemoryInventoryService>();
        return services;
    }

    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration) =>
        services.AddOrderEventPublishing(configuration).AddFunctionInfrastructure(configuration);
}
