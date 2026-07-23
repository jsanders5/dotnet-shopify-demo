using InventorySync.Api.Services;

namespace InventorySync.Api.Tests;

// Deterministic fake: the same input text always produces the same
// vector, and different texts produce different vectors, so similarity
// ranking in tests is meaningful without any real network call.
public class FakeVoyageEmbeddingClient : IVoyageEmbeddingClient
{
    public Task<float[]> EmbedAsync(string text, CancellationToken cancellationToken = default)
    {
        var random = new Random(text.GetHashCode());
        var vector = new float[8];
        for (var i = 0; i < vector.Length; i++)
        {
            vector[i] = (float)random.NextDouble();
        }
        return Task.FromResult(vector);
    }
}
