using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using OrderProcessing.Application.UseCases;

namespace Inventory.Functions;

public sealed class RetryFailedEvents(
    RetryFailedEventsUseCase retryFailedEvents,
    ILogger<RetryFailedEvents> logger)
{
    [Function(nameof(RetryFailedEvents))]
    public async Task Run(
        [TimerTrigger("%RetrySchedule%", RunOnStartup = false)] TimerInfo timer,
        CancellationToken cancellationToken)
    {
        logger.LogInformation("Starting scheduled failed-event retry at {UtcNow}.", DateTimeOffset.UtcNow);
        await retryFailedEvents.ExecuteAsync(cancellationToken);
    }
}
