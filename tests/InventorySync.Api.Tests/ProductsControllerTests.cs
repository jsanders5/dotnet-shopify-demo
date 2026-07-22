using System.Net;
using System.Net.Http.Json;
using InventorySync.Api.Models;
using Xunit;

namespace InventorySync.Api.Tests;

public class ProductsControllerTests : IClassFixture<InMemoryApiFactory>
{
    private readonly HttpClient _client;

    public ProductsControllerTests(InMemoryApiFactory factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task Create_ThenGetById_ReturnsSameProduct()
    {
        var newProduct = new Product
        {
            ShopifyInventoryItemId = 808950810,
            Title = "Crusher Evo",
            Sku = "CRUSH-EVO-BLK",
            Quantity = 10
        };

        var createResponse = await _client.PostAsJsonAsync("/api/products", newProduct);
        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);

        var created = await createResponse.Content.ReadFromJsonAsync<Product>();
        Assert.NotNull(created);

        var getResponse = await _client.GetAsync($"/api/products/{created!.Id}");
        Assert.Equal(HttpStatusCode.OK, getResponse.StatusCode);

        var fetched = await getResponse.Content.ReadFromJsonAsync<Product>();
        Assert.Equal(newProduct.Sku, fetched!.Sku);
    }

    [Fact]
    public async Task GetById_ReturnsNotFound_ForUnknownId()
    {
        var response = await _client.GetAsync("/api/products/999999");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task GetAll_IncludesCreatedProduct()
    {
        var newProduct = new Product
        {
            ShopifyInventoryItemId = 111222333,
            Title = "Push Active",
            Sku = "PUSH-ACT-GRY",
            Quantity = 20
        };
        await _client.PostAsJsonAsync("/api/products", newProduct);

        var all = await _client.GetFromJsonAsync<List<Product>>("/api/products");

        Assert.NotNull(all);
        Assert.Contains(all!, p => p.Sku == "PUSH-ACT-GRY");
    }
}
