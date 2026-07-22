using System.Text.Json.Serialization;

namespace InventorySync.Api.Models;

public record InventoryUpdatePayload(
    [property: JsonPropertyName("inventory_item_id")] long InventoryItemId,
    [property: JsonPropertyName("available")] int Available);
