using OrderProcessing.Application.Abstractions;
using OrderProcessing.Domain.Entities;
using OrderProcessing.Domain.Exceptions;
using OrderProcessing.Infrastructure.Options;
using Microsoft.Extensions.Options;

namespace OrderProcessing.Infrastructure.Services;

public sealed class InMemoryInventoryService : IInventoryService
{
    private readonly object _gate = new();
    private readonly Dictionary<string, InventoryItem> _inventory = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _processedEventIds = new(StringComparer.Ordinal);
    private readonly string? _failingSku;
    private readonly int _initialStock;

    public InMemoryInventoryService(IOptions<InventoryOptions> options)
    {
        _failingSku = options.Value.InventoryFailingSku;
        _initialStock = options.Value.InitialStock;
    }

    public Task ApplyAsync(OrderEvent orderEvent, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            if (_processedEventIds.Contains(orderEvent.EventId))
                return Task.CompletedTask;

            if (!string.IsNullOrWhiteSpace(_failingSku) &&
                string.Equals(orderEvent.Sku, _failingSku, StringComparison.OrdinalIgnoreCase))
                throw new TransientProcessingException($"Inventory temporarily unavailable for SKU '{orderEvent.Sku}'.");

            if (!_inventory.TryGetValue(orderEvent.Sku, out var item))
            {
                item = new InventoryItem(orderEvent.Sku, _initialStock);
                _inventory.Add(orderEvent.Sku, item);
            }

            if (orderEvent.EventType == OrderEventType.OrderCreated)
                item.Remove(orderEvent.Quantity);
            else
                item.Add(orderEvent.Quantity);

            _processedEventIds.Add(orderEvent.EventId);
        }

        return Task.CompletedTask;
    }
}
