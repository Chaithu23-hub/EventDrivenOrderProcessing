using OrderProcessing.Domain.Entities;

namespace OrderProcessing.Application.Abstractions;

public interface IOrderEventPublisher
{
    Task PublishAsync(OrderEvent orderEvent, CancellationToken cancellationToken = default);
}
