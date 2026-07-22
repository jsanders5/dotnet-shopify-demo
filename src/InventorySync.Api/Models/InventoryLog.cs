namespace InventorySync.Api.Models;

public class InventoryLog
{
    public int Id { get; set; }
    public int ProductId { get; set; }
    public Product? Product { get; set; }
    public int PreviousQuantity { get; set; }
    public int NewQuantity { get; set; }
    public DateTime ReceivedAtUtc { get; set; } = DateTime.UtcNow;
}
