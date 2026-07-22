using System.Net.Http.Json;
using InventorySync.Api.Data;
using InventorySync.Api.Models;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace InventorySync.Api.Tests;

public class LowStockStatusEndpointTests : IClassFixture<InMemoryApiFactory>
{
    private readonly HttpClient _client;
    private readonly InMemoryApiFactory _factory;

    public LowStockStatusEndpointTests(InMemoryApiFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task ReturnsIsLowStockTrue_WhenQuantityAtOrBelowThreshold()
    {
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.Products.Add(new Product
            {
                ShopifyInventoryItemId = 555111,
                Title = "Low Item",
                Sku = "LOW-ITEM",
                Quantity = 2,
                LowStockThreshold = 5
            });
            db.SaveChanges();
        }

        var result = await _client.GetFromJsonAsync<LowStockStatus>(
            "/api/products/by-inventory-item/555111");

        Assert.NotNull(result);
        Assert.True(result!.Tracked);
        Assert.True(result.IsLowStock);
        Assert.Equal(2, result.Quantity);
    }

    [Fact]
    public async Task ReturnsIsLowStockFalse_WhenQuantityAboveThreshold()
    {
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.Products.Add(new Product
            {
                ShopifyInventoryItemId = 555222,
                Title = "High Item",
                Sku = "HIGH-ITEM",
                Quantity = 50,
                LowStockThreshold = 5
            });
            db.SaveChanges();
        }

        var result = await _client.GetFromJsonAsync<LowStockStatus>(
            "/api/products/by-inventory-item/555222");

        Assert.NotNull(result);
        Assert.True(result!.Tracked);
        Assert.False(result.IsLowStock);
    }

    [Fact]
    public async Task ReturnsTrackedFalse_ForUnknownInventoryItemId_NotNotFound()
    {
        var response = await _client.GetAsync("/api/products/by-inventory-item/999999999");
        Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);

        var result = await response.Content.ReadFromJsonAsync<LowStockStatus>();
        Assert.NotNull(result);
        Assert.False(result!.Tracked);
        Assert.False(result.IsLowStock);
    }
}
