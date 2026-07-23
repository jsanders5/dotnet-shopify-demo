namespace InventorySync.Api.Models;

public record AskResponse(
    string Question,
    string AnswerWithoutContext,
    string AnswerWithContext,
    List<string> RetrievedChunks);
