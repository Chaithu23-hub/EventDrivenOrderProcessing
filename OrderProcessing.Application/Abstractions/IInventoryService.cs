using OrderProcessing.Domain.Entities;

namespace OrderProcessing.Application.Abstractions;

public interface IInventoryService
{
    Task ApplyAsync(OrderEvent orderEvent, CancellationToken cancellationToken = default);
}
