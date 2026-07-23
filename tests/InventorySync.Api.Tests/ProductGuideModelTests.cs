using InventorySync.Api.Data;
using InventorySync.Api.Models;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace InventorySync.Api.Tests;

public class ProductGuideModelTests
{
    [Fact]
    public async Task CanAddGuideWithChunks_AndLoadThemBack()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        await using var db = new AppDbContext(options);
        db.ProductGuides.Add(new ProductGuide
        {
            ShopifyProductId = 12345,
            Title = "Test Headphones",
            Chunks = new List<ProductGuideChunk>
            {
                new() { Content = "Chunk one text", Embedding = "[0.1,0.2,0.3]" },
                new() { Content = "Chunk two text", Embedding = "[0.4,0.5,0.6]" }
            }
        });
        await db.SaveChangesAsync();

        var loaded = await db.ProductGuides
            .Include(g => g.Chunks)
            .SingleAsync(g => g.ShopifyProductId == 12345);

        Assert.Equal("Test Headphones", loaded.Title);
        Assert.Equal(2, loaded.Chunks.Count);
    }
}
