using OrderProcessing.Domain.Exceptions;

namespace OrderProcessing.Domain.Entities;

public sealed class InventoryItem(string sku, int quantity)
{
    public string Sku { get; } = sku;
    public int Quantity { get; private set; } = quantity;

    public void Remove(int quantity)
    {
        if (quantity <= 0)
            throw new DomainException("Quantity must be greater than zero.");
        if (Quantity < quantity)
            throw new DomainException($"Insufficient stock for SKU '{Sku}'.");

        Quantity -= quantity;
    }

    public void Add(int quantity)
    {
        if (quantity <= 0)
            throw new DomainException("Quantity must be greater than zero.");

        Quantity = checked(Quantity + quantity);
    }
}
