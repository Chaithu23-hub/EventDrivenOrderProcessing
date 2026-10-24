using Microsoft.Extensions.Options;
using OrderProcessing.Domain.Entities;
using OrderProcessing.Domain.Exceptions;
using OrderProcessing.Infrastructure.Options;
using OrderProcessing.Infrastructure.Services;

namespace Application.UnitTests;

public sealed class InMemoryInventoryServiceTests
{
    [Fact]
    public async Task ApplyAsync_WhenEventIdIsRepeated_DoesNotApplyCancellationTwice()
    {
        var inventory = new InMemoryInventoryService(Options.Create(new InventoryOptions { InitialStock = 1 }));
        var orderCreated = new OrderEvent(
            "created-1", "order-1", "SKU-1", 1, OrderEventType.OrderCreated, "corr-1", DateTimeOffset.UtcNow);
        var orderCancelled = orderCreated with
        {
            EventId = "cancelled-1",
            EventType = OrderEventType.OrderCancelled
        };

        await inventory.ApplyAsync(orderCreated);
        await inventory.ApplyAsync(orderCancelled);
        await inventory.ApplyAsync(orderCancelled);

        var outOfStock = orderCreated with { EventId = "created-2", Quantity = 2 };
        await Assert.ThrowsAsync<DomainException>(() => inventory.ApplyAsync(outOfStock));
    }
}
