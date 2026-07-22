using System.Net;
using System.Net.Http.Json;
using InventorySync.Api.Data;
using InventorySync.Api.Models;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace InventorySync.Api.Tests;

public class LowStockStatusByVariantEndpointTests : IClassFixture<InMemoryApiFactory>
{
    private readonly HttpClient _client;
    private readonly InMemoryApiFactory _factory;

    public LowStockStatusByVariantEndpointTests(InMemoryApiFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task ReturnsIsLowStockTrue_ForTrackedVariant_WhenQuantityAtOrBelowThreshold()
    {
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.Products.Add(new Product
            {
                ShopifyInventoryItemId = 777111,
                ShopifyVariantId = 888111,
                Title = "Low Variant Item",
                Sku = "LOW-VARIANT",
                Quantity = 2,
                LowStockThreshold = 5
            });
            db.SaveChanges();
        }

        var result = await _client.GetFromJsonAsync<LowStockStatus>(
            "/api/products/by-variant/888111");

        Assert.NotNull(result);
        Assert.True(result!.Tracked);
        Assert.True(result.IsLowStock);
    }

    [Fact]
    public async Task ReturnsTrackedFalse_ForUnknownVariantId_NotNotFound()
    {
        var response = await _client.GetAsync("/api/products/by-variant/999999999");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var result = await response.Content.ReadFromJsonAsync<LowStockStatus>();
        Assert.NotNull(result);
        Assert.False(result!.Tracked);
        Assert.False(result.IsLowStock);
    }
}
