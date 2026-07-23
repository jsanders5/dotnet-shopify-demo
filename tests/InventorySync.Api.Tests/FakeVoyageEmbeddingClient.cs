using InventorySync.Api.Services;

namespace InventorySync.Api.Tests;

// Deterministic fake: the same input text always produces the same
// vector (a stable hash of the UTF-8 bytes seeds Random, NOT
// string.GetHashCode() - that's randomized per-process in modern .NET
// and would not be stable across separate test runs), and different
// texts produce different vectors, so similarity ranking in tests is
// meaningful without any real network call.
public class FakeVoyageEmbeddingClient : IVoyageEmbeddingClient
{
    public Task<float[]> EmbedAsync(string text, CancellationToken cancellationToken = default)
    {
        var seed = StableHash(text);
        var random = new Random(seed);
        var vector = new float[8];
        for (var i = 0; i < vector.Length; i++)
        {
            vector[i] = (float)random.NextDouble();
        }
        return Task.FromResult(vector);
    }

    // FNV-1a 32-bit hash - deterministic across processes, unlike
    // string.GetHashCode().
    private static int StableHash(string text)
    {
        unchecked
        {
            const uint fnvPrime = 16777619;
            const uint fnvOffsetBasis = 2166136261;
            var hash = fnvOffsetBasis;
            foreach (var b in System.Text.Encoding.UTF8.GetBytes(text))
            {
                hash ^= b;
                hash *= fnvPrime;
            }
            return (int)hash;
        }
    }
}
