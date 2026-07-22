using System.Net;
using System.Security.Cryptography;
using System.Text;
using InventorySync.Api.Data;
using InventorySync.Api.Models;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace InventorySync.Api.Tests;

public class WebhooksControllerTests : IClassFixture<InMemoryApiFactory>
{
    private const string Secret = "test-webhook-secret";
    private readonly InMemoryApiFactory _factory;

    public WebhooksControllerTests(InMemoryApiFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task InventoryUpdate_UpdatesQuantityAndLogsChange_ForValidSignature()
    {
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.Products.Add(new Product
            {
                ShopifyInventoryItemId = 808950810,
                Title = "Crusher Evo",
                Sku = "CRUSH-EVO-BLK",
                Quantity = 10
            });
            db.SaveChanges();
        }

        var body = "{\"inventory_item_id\":808950810,\"available\":3}";
        var signature = ComputeHmac(Secret, Encoding.UTF8.GetBytes(body));

        var client = _factory.CreateClient();
        var request = new HttpRequestMessage(HttpMethod.Post, "/webhooks/inventory-update")
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        };
        request.Headers.Add("X-Shopify-Hmac-Sha256", signature);

        var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var verifyScope = _factory.Services.CreateScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<AppDbContext>();
        var product = verifyDb.Products.Single(p => p.ShopifyInventoryItemId == 808950810);
        Assert.Equal(3, product.Quantity);
        Assert.Single(verifyDb.InventoryLogs);
    }

    [Fact]
    public async Task InventoryUpdate_ReturnsUnauthorized_ForBadSignature()
    {
        var body = "{\"inventory_item_id\":808950810,\"available\":3}";
        var client = _factory.CreateClient();
        var request = new HttpRequestMessage(HttpMethod.Post, "/webhooks/inventory-update")
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        };
        request.Headers.Add("X-Shopify-Hmac-Sha256", "not-a-valid-signature");

        var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    private static string ComputeHmac(string secret, byte[] body)
    {
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        return Convert.ToBase64String(hmac.ComputeHash(body));
    }
}
