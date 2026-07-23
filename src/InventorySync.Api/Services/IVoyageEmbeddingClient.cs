namespace InventorySync.Api.Services;

public interface IVoyageEmbeddingClient
{
    Task<float[]> EmbedAsync(string text, CancellationToken cancellationToken = default);
}
