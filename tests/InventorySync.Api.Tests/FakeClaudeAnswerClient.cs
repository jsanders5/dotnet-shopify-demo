using InventorySync.Api.Services;

namespace InventorySync.Api.Tests;

public class FakeClaudeAnswerClient : IClaudeAnswerClient
{
    public Task<string> AskAsync(string question, string? context, CancellationToken cancellationToken = default)
    {
        var answer = context is null
            ? $"[ungrounded answer to: {question}]"
            : $"[grounded answer to: {question}, using: {context}]";
        return Task.FromResult(answer);
    }
}
