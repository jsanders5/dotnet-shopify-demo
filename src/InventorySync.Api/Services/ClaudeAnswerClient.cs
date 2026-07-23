using System.Text;
using System.Text.Json;

namespace InventorySync.Api.Services;

public class ClaudeAnswerClient : IClaudeAnswerClient
{
    private readonly HttpClient _httpClient;
    private readonly IConfiguration _config;

    public ClaudeAnswerClient(HttpClient httpClient, IConfiguration config)
    {
        _httpClient = httpClient;
        _config = config;
    }

    public async Task<string> AskAsync(string question, string? context, CancellationToken cancellationToken = default)
    {
        var apiKey = _config["Anthropic:ApiKey"];
        var prompt = context is null
            ? question
            : $"Use the following product documentation to answer the question. " +
              $"If the documentation doesn't mention a feature, say plainly that " +
              $"this product doesn't have it - don't guess.\n\nDocumentation:\n{context}\n\nQuestion: {question}";

        var request = new HttpRequestMessage(HttpMethod.Post, "https://api.anthropic.com/v1/messages");
        request.Headers.Add("x-api-key", apiKey);
        request.Headers.Add("anthropic-version", "2023-06-01");
        var body = JsonSerializer.Serialize(new
        {
            model = "claude-haiku-4-5-20251001",
            max_tokens = 512,
            messages = new[] { new { role = "user", content = prompt } }
        });
        request.Content = new StringContent(body, Encoding.UTF8, "application/json");

        var response = await _httpClient.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();

        var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
        using var doc = JsonDocument.Parse(responseBody);
        return doc.RootElement.GetProperty("content")[0].GetProperty("text").GetString() ?? string.Empty;
    }
}
