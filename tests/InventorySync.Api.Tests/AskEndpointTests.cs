using System.Net;
using System.Net.Http.Json;
using InventorySync.Api.Data;
using InventorySync.Api.Models;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace InventorySync.Api.Tests;

public class AskEndpointTests : IClassFixture<InMemoryApiFactory>
{
    private readonly HttpClient _client;
    private readonly InMemoryApiFactory _factory;

    public AskEndpointTests(InMemoryApiFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task ReturnsBothAnswersAndRetrievedChunks_ForKnownProduct()
    {
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.ProductGuides.Add(new ProductGuide
            {
                ShopifyProductId = 999111,
                Title = "Ask Test Headphones",
                Chunks = new List<ProductGuideChunk>
                {
                    new() { Content = "Battery lasts 30 hours.", Embedding = "[1,0,0,0,0,0,0,0]" },
                    new() { Content = "Pairing is automatic on first power-on.", Embedding = "[0,1,0,0,0,0,0,0]" }
                }
            });
            db.SaveChanges();
        }

        var response = await _client.PostAsJsonAsync(
            "/api/products/999111/ask", new AskRequest("How long does the battery last?"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<AskResponse>();

        Assert.NotNull(result);
        Assert.False(string.IsNullOrEmpty(result!.AnswerWithoutContext));
        Assert.False(string.IsNullOrEmpty(result.AnswerWithContext));
        Assert.NotEmpty(result.RetrievedChunks);
    }

    [Fact]
    public async Task ReturnsNotFound_ForUnknownProduct()
    {
        var response = await _client.PostAsJsonAsync(
            "/api/products/888222/ask", new AskRequest("Does this have ANC?"));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task ReturnsBadRequest_ForBlankQuestion()
    {
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.ProductGuides.Add(new ProductGuide
            {
                ShopifyProductId = 777333,
                Title = "Blank Question Test",
                Chunks = new List<ProductGuideChunk>
                {
                    new() { Content = "Some content.", Embedding = "[1,0,0,0,0,0,0,0]" }
                }
            });
            db.SaveChanges();
        }

        var response = await _client.PostAsJsonAsync(
            "/api/products/777333/ask", new AskRequest(""));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}
