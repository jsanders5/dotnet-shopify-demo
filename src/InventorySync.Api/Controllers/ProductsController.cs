using InventorySync.Api.Data;
using InventorySync.Api.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace InventorySync.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class ProductsController : ControllerBase
{
    private readonly AppDbContext _db;

    public ProductsController(AppDbContext db)
    {
        _db = db;
    }

    [HttpGet]
    public async Task<ActionResult<IEnumerable<Product>>> GetAll()
    {
        return await _db.Products.AsNoTracking().ToListAsync();
    }

    [HttpGet("{id:int}")]
    public async Task<ActionResult<Product>> GetById(int id)
    {
        var product = await _db.Products.FindAsync(id);
        return product is null ? NotFound() : product;
    }

    [HttpPost]
    public async Task<ActionResult<Product>> Create(Product product)
    {
        _db.Products.Add(product);
        await _db.SaveChangesAsync();
        return CreatedAtAction(nameof(GetById), new { id = product.Id }, product);
    }

    [HttpGet("low-stock")]
    public async Task<ActionResult<IEnumerable<Product>>> GetLowStock()
    {
        return await _db.Products
            .FromSqlRaw("EXEC dbo.GetLowStockProducts")
            .ToListAsync();
    }

    [HttpGet("by-inventory-item/{inventoryItemId:long}")]
    public async Task<ActionResult<LowStockStatus>> GetByInventoryItem(long inventoryItemId)
    {
        var product = await _db.Products.AsNoTracking()
            .FirstOrDefaultAsync(p => p.ShopifyInventoryItemId == inventoryItemId);

        if (product is null)
        {
            return new LowStockStatus(inventoryItemId, Tracked: false, Quantity: null,
                LowStockThreshold: null, IsLowStock: false);
        }

        return new LowStockStatus(
            inventoryItemId,
            Tracked: true,
            Quantity: product.Quantity,
            LowStockThreshold: product.LowStockThreshold,
            IsLowStock: product.Quantity <= product.LowStockThreshold);
    }

    // Shopify's storefront Liquid does not expose variant.inventory_item_id
    // (confirmed live against a real theme/store), only variant.id — so the
    // storefront badge looks products up by variant ID instead. The webhook
    // receiver is unaffected: it still keys on ShopifyInventoryItemId, which
    // is what Shopify's real inventory_levels/update payload actually carries.
    [HttpGet("by-variant/{variantId:long}")]
    public async Task<ActionResult<LowStockStatus>> GetByVariant(long variantId)
    {
        var product = await _db.Products.AsNoTracking()
            .FirstOrDefaultAsync(p => p.ShopifyVariantId == variantId);

        if (product is null)
        {
            return new LowStockStatus(0, Tracked: false, Quantity: null,
                LowStockThreshold: null, IsLowStock: false);
        }

        return new LowStockStatus(
            product.ShopifyInventoryItemId,
            Tracked: true,
            Quantity: product.Quantity,
            LowStockThreshold: product.LowStockThreshold,
            IsLowStock: product.Quantity <= product.LowStockThreshold);
    }
}
