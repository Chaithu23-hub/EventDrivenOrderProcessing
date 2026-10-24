using Microsoft.Extensions.DependencyInjection;
using OrderProcessing.Application.Options;
using OrderProcessing.Application.UseCases;

namespace OrderProcessing.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddOptions<RetryOptions>()
            .BindConfiguration("")
            .Validate(options => options.MaxRetryCount > 0, "MaxRetryCount must be greater than zero.")
            .Validate(options => options.BatchSize > 0, "BatchSize must be greater than zero.")
            .ValidateOnStart();
        services.AddScoped<ProcessOrderEventUseCase>();
        services.AddScoped<RetryFailedEventsUseCase>();
        return services;
    }
}
