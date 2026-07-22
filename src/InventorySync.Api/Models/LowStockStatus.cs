namespace InventorySync.Api.Models;

public record LowStockStatus(
    long InventoryItemId,
    bool Tracked,
    int? Quantity,
    int? LowStockThreshold,
    bool IsLowStock);
