using InventorySync.Api.Data;
using InventorySync.Api.Models;
using InventorySync.Api.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace InventorySync.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class ProductsController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly IVoyageEmbeddingClient _embeddingClient;
    private readonly IClaudeAnswerClient _answerClient;
    private readonly ILogger<ProductsController> _logger;

    public ProductsController(AppDbContext db, IVoyageEmbeddingClient embeddingClient, IClaudeAnswerClient answerClient, ILogger<ProductsController> logger)
    {
        _db = db;
        _embeddingClient = embeddingClient;
        _answerClient = answerClient;
        _logger = logger;
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

    [HttpPost("{shopifyProductId:long}/ask")]
    public async Task<ActionResult<AskResponse>> Ask(long shopifyProductId, AskRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Question)) return BadRequest();

        var guide = await _db.ProductGuides
            .Include(g => g.Chunks)
            .FirstOrDefaultAsync(g => g.ShopifyProductId == shopifyProductId);
        if (guide is null || guide.Chunks.Count == 0) return NotFound();

        try
        {
            var questionEmbedding = await _embeddingClient.EmbedAsync(request.Question);

            var topChunks = guide.Chunks
                .Select(c => new
                {
                    c.Content,
                    Similarity = CosineSimilarity.Compute(
                        questionEmbedding, JsonSerializer.Deserialize<float[]>(c.Embedding)!)
                })
                .OrderByDescending(x => x.Similarity)
                .Take(3)
                .Select(x => x.Content)
                .ToList();

            var contextText = string.Join("\n\n", topChunks);

            // The ungrounded call still gets the product name - that's known
            // from the product page regardless of RAG, and omitting it would
            // test "does the model know which product this is" rather than
            // the actual comparison this endpoint is for: accurate product
            // documentation vs. none.
            var namedQuestion = $"Product: {guide.Title}\n\nQuestion: {request.Question}";
            var answerWithoutContext = await _answerClient.AskAsync(namedQuestion, null);
            var answerWithContext = await _answerClient.AskAsync(request.Question, contextText);

            return new AskResponse(request.Question, answerWithoutContext, answerWithContext, topChunks);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            // Voyage/Claude failure (bad key, rate limit, timeout) or a
            // malformed response body. No retry logic - disclosed
            // simplification - but the caller gets a clear error instead of
            // an unhandled exception. The exception detail is logged, not
            // returned - it can carry internal endpoint/wiring details and
            // this endpoint is reachable through the public tunnel.
            _logger.LogError(ex, "Ask failed for product {ShopifyProductId}", shopifyProductId);
            return StatusCode(502, new { error = "The Q&A service is temporarily unavailable. Please try again shortly." });
        }
    }
}
