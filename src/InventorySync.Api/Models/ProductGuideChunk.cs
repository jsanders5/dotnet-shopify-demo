namespace InventorySync.Api.Models;

public class ProductGuideChunk
{
    public int Id { get; set; }
    public int ProductGuideId { get; set; }
    public ProductGuide? ProductGuide { get; set; }
    public required string Content { get; set; }
    // JSON-serialized float[] - the chunk's Voyage AI embedding, computed
    // once at seed time (Task 7), never recomputed per-request (Task 5
    // only embeds the incoming question).
    public required string Embedding { get; set; }
}
