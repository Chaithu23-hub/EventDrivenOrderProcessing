using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using OrderProcessing.Application.Abstractions;
using OrderProcessing.Application.DTOs;
using OrderProcessing.Application.Options;
using OrderProcessing.Application.UseCases;
using OrderProcessing.Domain.Entities;
using OrderProcessing.Domain.Exceptions;

namespace Application.UnitTests;

public sealed class RetryFailedEventsUseCaseTests
{
    [Fact]
    public async Task ExecuteAsync_WhenRetrySucceeds_UpdatesLogToSuccess()
    {
        var repository = new Mock<IEventLogRepository>();
        var inventory = new Mock<IInventoryService>();
        var eventLog = CreateFailedLog();
        var upserts = new List<EventLog>();
        SetupQueries(repository, [eventLog], []);
        repository.Setup(item => item.UpsertAsync(It.IsAny<EventLog>(), It.IsAny<CancellationToken>()))
            .Callback<EventLog, CancellationToken>((log, _) => upserts.Add(log))
            .Returns(Task.CompletedTask);
        inventory.Setup(item => item.ApplyAsync(It.IsAny<OrderEvent>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        var process = new ProcessOrderEventUseCase(repository.Object, inventory.Object,
            Mock.Of<ILogger<ProcessOrderEventUseCase>>());
        var retry = CreateRetryUseCase(repository.Object, process);

        await retry.ExecuteAsync();

        var success = Assert.Single(upserts);
        Assert.Equal(EventStatus.Success, success.Status);
        Assert.Equal(0, success.RetryCount);
        Assert.NotNull(success.ProcessedAt);
    }

    [Fact]
    public async Task ExecuteAsync_WhenRetryFails_IncrementsRetryCountAndKeepsFailure()
    {
        var repository = new Mock<IEventLogRepository>();
        var inventory = new Mock<IInventoryService>();
        var eventLog = CreateFailedLog(retryCount: 2);
        var upserts = new List<EventLog>();
        SetupQueries(repository, [eventLog], []);
        repository.Setup(item => item.UpsertAsync(It.IsAny<EventLog>(), It.IsAny<CancellationToken>()))
            .Callback<EventLog, CancellationToken>((log, _) => upserts.Add(log))
            .Returns(Task.CompletedTask);
        inventory.Setup(item => item.ApplyAsync(It.IsAny<OrderEvent>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new TransientProcessingException("still unavailable"));
        var process = new ProcessOrderEventUseCase(repository.Object, inventory.Object,
            Mock.Of<ILogger<ProcessOrderEventUseCase>>());
        var retry = CreateRetryUseCase(repository.Object, process);

        await retry.ExecuteAsync();

        Assert.Equal(2, upserts.Count);
        var failed = upserts[^1];
        Assert.Equal(EventStatus.Failed, failed.Status);
        Assert.Equal(3, failed.RetryCount);
        Assert.Equal("still unavailable", failed.ErrorMessage);
        Assert.Null(failed.ProcessedAt);
    }

    [Fact]
    public async Task ExecuteAsync_WhenMaxRetryCountReached_DoesNotProcessEvent()
    {
        var repository = new Mock<IEventLogRepository>();
        var inventory = new Mock<IInventoryService>();
        var exhausted = CreateFailedLog(retryCount: 5);
        SetupQueries(repository, [], [exhausted]);
        var process = new ProcessOrderEventUseCase(repository.Object, inventory.Object,
            Mock.Of<ILogger<ProcessOrderEventUseCase>>());
        var retry = CreateRetryUseCase(repository.Object, process);

        await retry.ExecuteAsync();

        inventory.Verify(item => item.ApplyAsync(It.IsAny<OrderEvent>(), It.IsAny<CancellationToken>()), Times.Never);
        repository.Verify(item => item.GetFailedAsync(5, 20, It.IsAny<CancellationToken>()), Times.Once);
    }

    private static RetryFailedEventsUseCase CreateRetryUseCase(
        IEventLogRepository repository,
        ProcessOrderEventUseCase process) =>
        new(repository, process, Options.Create(new RetryOptions { MaxRetryCount = 5, BatchSize = 20 }),
            Mock.Of<ILogger<RetryFailedEventsUseCase>>());

    private static void SetupQueries(
        Mock<IEventLogRepository> repository,
        IReadOnlyList<EventLog> failed,
        IReadOnlyList<EventLog> exhausted)
    {
        repository.Setup(item => item.GetFailedAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(failed);
        repository.Setup(item => item.GetExhaustedFailedAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(exhausted);
    }

    private static EventLog CreateFailedLog(int retryCount = 0)
    {
        var orderEvent = ProcessOrderEventUseCaseTests.CreateEvent();
        return new EventLog
        {
            Id = orderEvent.EventId,
            OrderId = orderEvent.OrderId,
            EventType = orderEvent.EventType.ToString(),
            Payload = JsonSerializer.Serialize(OrderEventPayload.From(orderEvent)),
            Status = EventStatus.Failed,
            RetryCount = retryCount,
            ErrorMessage = "previous failure",
            CreatedAt = orderEvent.CreatedAt,
            LastAttemptAt = DateTimeOffset.UtcNow,
            CorrelationId = orderEvent.CorrelationId
        };
    }
}
