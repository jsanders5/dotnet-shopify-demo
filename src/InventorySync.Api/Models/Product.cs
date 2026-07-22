namespace InventorySync.Api.Models;

public class Product
{
    public int Id { get; set; }
    public required long ShopifyInventoryItemId { get; set; }
    // The real Shopify inventory_levels/update webhook only carries
    // inventory_item_id, but Shopify's storefront Liquid environment does not
    // expose inventory_item_id on the variant object at all (confirmed live,
    // including via `{{ variant | json }}` — it's simply not there). Liquid
    // does expose variant.id, so the storefront looks products up by that
    // instead; the webhook receiver keeps using ShopifyInventoryItemId,
    // unchanged.
    public long? ShopifyVariantId { get; set; }
    public required string Title { get; set; }
    public required string Sku { get; set; }
    public int Quantity { get; set; }
    public int LowStockThreshold { get; set; } = 5;
}
