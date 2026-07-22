using InventorySync.Api.Data;
using InventorySync.Api.Models;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace InventorySync.Api.Tests;

public class AppDbContextTests
{
    [Fact]
    public async Task CanAddAndRetrieveProduct()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        await using var db = new AppDbContext(options);
        db.Products.Add(new Product
        {
            ShopifyInventoryItemId = 808950810,
            Title = "Crusher Evo",
            Sku = "CRUSH-EVO-BLK",
            Quantity = 10
        });
        await db.SaveChangesAsync();

        var stored = await db.Products.SingleAsync();
        Assert.Equal("CRUSH-EVO-BLK", stored.Sku);
        Assert.Equal(5, stored.LowStockThreshold); // default
    }
}
