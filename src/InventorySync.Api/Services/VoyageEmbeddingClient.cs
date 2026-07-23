using System.Text;
using System.Text.Json;

namespace InventorySync.Api.Services;

public class VoyageEmbeddingClient : IVoyageEmbeddingClient
{
    private readonly HttpClient _httpClient;
    private readonly IConfiguration _config;

    public VoyageEmbeddingClient(HttpClient httpClient, IConfiguration config)
    {
        _httpClient = httpClient;
        _config = config;
    }

    public async Task<float[]> EmbedAsync(string text, CancellationToken cancellationToken = default)
    {
        var apiKey = _config["VoyageAi:ApiKey"];
        var request = new HttpRequestMessage(HttpMethod.Post, "https://api.voyageai.com/v1/embeddings");
        request.Headers.Add("Authorization", $"Bearer {apiKey}");
        var body = JsonSerializer.Serialize(new { input = new[] { text }, model = "voyage-3.5" });
        request.Content = new StringContent(body, Encoding.UTF8, "application/json");

        var response = await _httpClient.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();

        var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
        using var doc = JsonDocument.Parse(responseBody);
        var embeddingArray = doc.RootElement.GetProperty("data")[0].GetProperty("embedding");
        return embeddingArray.EnumerateArray().Select(e => e.GetSingle()).ToArray();
    }
}
