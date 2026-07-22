using System.Text;
using System.Text.Json;
using InventorySync.Api.Data;
using InventorySync.Api.Models;
using InventorySync.Api.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace InventorySync.Api.Controllers;

[ApiController]
[Route("webhooks")]
public class WebhooksController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly IConfiguration _config;

    public WebhooksController(AppDbContext db, IConfiguration config)
    {
        _db = db;
        _config = config;
    }

    [HttpPost("inventory-update")]
    public async Task<IActionResult> InventoryUpdate()
    {
        Request.EnableBuffering();
        using var reader = new StreamReader(Request.Body, leaveOpen: true);
        var rawBody = await reader.ReadToEndAsync();
        Request.Body.Position = 0;

        var secret = _config["Shopify:WebhookSecret"];
        var hmacHeader = Request.Headers["X-Shopify-Hmac-Sha256"].ToString();

        if (string.IsNullOrEmpty(secret) ||
            !ShopifyHmacVerifier.IsValid(secret, Encoding.UTF8.GetBytes(rawBody), hmacHeader))
        {
            return Unauthorized();
        }

        var payload = JsonSerializer.Deserialize<InventoryUpdatePayload>(rawBody);
        if (payload is null) return BadRequest();

        var product = await _db.Products
            .FirstOrDefaultAsync(p => p.ShopifyInventoryItemId == payload.InventoryItemId);
        // A real store fires inventory_levels/update for every inventory item, most of
        // which this demo won't have a Product row for. Acknowledge with 200 rather than
        // 404 so Shopify doesn't treat an untracked item as a delivery failure and retry
        // it (Shopify retries non-2xx responses for up to ~48h).
        if (product is null) return Ok();

        // Known, disclosed limitation: Shopify's real inventory_levels/update payload
        // also carries a location_id, since `available` is the count at ONE location,
        // not a store-wide total. This demo assumes single-location inventory and
        // overwrites Quantity directly - for a real multi-location product, a webhook
        // from one location would clobber another location's contribution instead of
        // summing them. A correct implementation would track quantity per
        // (Product, Location) and derive the total via SUM. Not implemented here;
        // scoped out deliberately rather than silently ignored - see README.
        var previousQuantity = product.Quantity;
        product.Quantity = payload.Available;

        _db.InventoryLogs.Add(new InventoryLog
        {
            ProductId = product.Id,
            PreviousQuantity = previousQuantity,
            NewQuantity = payload.Available
        });

        await _db.SaveChangesAsync();
        return Ok();
    }
}
