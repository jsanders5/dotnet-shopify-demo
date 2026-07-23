namespace InventorySync.Api.Services;

public interface IClaudeAnswerClient
{
    Task<string> AskAsync(string question, string? context, CancellationToken cancellationToken = default);
}
