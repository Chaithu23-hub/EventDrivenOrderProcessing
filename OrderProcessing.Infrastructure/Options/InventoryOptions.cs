namespace OrderProcessing.Infrastructure.Options;

public sealed class InventoryOptions
{
    public string? InventoryFailingSku { get; set; }
    public int InitialStock { get; set; } = 100;
}
