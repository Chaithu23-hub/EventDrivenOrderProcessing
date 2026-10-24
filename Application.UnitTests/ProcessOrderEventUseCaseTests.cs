using Microsoft.Extensions.Logging;
using Moq;
using OrderProcessing.Application.Abstractions;
using OrderProcessing.Application.UseCases;
using OrderProcessing.Domain.Entities;
using OrderProcessing.Domain.Exceptions;

namespace Application.UnitTests;

public sealed class ProcessOrderEventUseCaseTests
{
    [Fact]
    public async Task ExecuteAsync_WhenInventorySucceeds_UpsertsSuccessLog()
    {
        var repository = new Mock<IEventLogRepository>();
        var inventory = new Mock<IInventoryService>();
        var logs = new List<EventLog>();
        repository.Setup(item => item.UpsertAsync(It.IsAny<EventLog>(), It.IsAny<CancellationToken>()))
            .Callback<EventLog, CancellationToken>((eventLog, _) => logs.Add(eventLog))
            .Returns(Task.CompletedTask);
        inventory.Setup(item => item.ApplyAsync(It.IsAny<OrderEvent>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        var useCase = CreateUseCase(repository.Object, inventory.Object);

        var result = await useCase.ExecuteAsync(CreateEvent());

        Assert.True(result.Succeeded);
        var log = Assert.Single(logs);
        Assert.Equal(EventStatus.Success, log.Status);
        Assert.Equal(0, log.RetryCount);
        Assert.NotNull(log.ProcessedAt);
        Assert.Equal("event-1", log.Id);
    }

    [Fact]
    public async Task ExecuteAsync_WhenInventoryKeepsFailing_RetriesThenWritesFailedLog()
    {
        var repository = new Mock<IEventLogRepository>();
        var inventory = new Mock<IInventoryService>();
        EventLog? savedLog = null;
        repository.Setup(item => item.UpsertAsync(It.IsAny<EventLog>(), It.IsAny<CancellationToken>()))
            .Callback<EventLog, CancellationToken>((eventLog, _) => savedLog = eventLog)
            .Returns(Task.CompletedTask);
        inventory.Setup(item => item.ApplyAsync(It.IsAny<OrderEvent>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new TransientProcessingException("inventory unavailable"));
        var useCase = CreateUseCase(repository.Object, inventory.Object);

        var result = await useCase.ExecuteAsync(CreateEvent());

        Assert.False(result.Succeeded);
        Assert.Equal("inventory unavailable", result.ErrorMessage);
        inventory.Verify(item => item.ApplyAsync(It.IsAny<OrderEvent>(), It.IsAny<CancellationToken>()), Times.Exactly(3));
        Assert.NotNull(savedLog);
        Assert.Equal(EventStatus.Failed, savedLog.Status);
        Assert.Equal(0, savedLog.RetryCount);
        Assert.Equal("inventory unavailable", savedLog.ErrorMessage);
        Assert.Null(savedLog.ProcessedAt);
    }

    private static ProcessOrderEventUseCase CreateUseCase(
        IEventLogRepository repository,
        IInventoryService inventory) =>
        new(repository, inventory, Mock.Of<ILogger<ProcessOrderEventUseCase>>());

    internal static OrderEvent CreateEvent() =>
        new("event-1", "order-1", "SKU-1", 2, OrderEventType.OrderCreated, "corr-1", DateTimeOffset.UtcNow);
}
