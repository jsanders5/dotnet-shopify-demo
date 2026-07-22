namespace InventorySync.Api.Models;

public class Product
{
    public int Id { get; set; }
    public required long ShopifyInventoryItemId { get; set; }
    public required string Title { get; set; }
    public required string Sku { get; set; }
    public int Quantity { get; set; }
    public int LowStockThreshold { get; set; } = 5;
}
