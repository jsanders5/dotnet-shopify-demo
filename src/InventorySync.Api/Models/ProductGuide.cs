namespace InventorySync.Api.Models;

public class ProductGuide
{
    public int Id { get; set; }
    public required long ShopifyProductId { get; set; }
    public required string Title { get; set; }
    public List<ProductGuideChunk> Chunks { get; set; } = new();
}
