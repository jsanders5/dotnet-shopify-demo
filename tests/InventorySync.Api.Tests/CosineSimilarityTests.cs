using InventorySync.Api.Services;
using Xunit;

namespace InventorySync.Api.Tests;

public class CosineSimilarityTests
{
    [Fact]
    public void Compute_ReturnsOne_ForIdenticalVectors()
    {
        var a = new float[] { 1f, 2f, 3f };
        var result = CosineSimilarity.Compute(a, a);
        Assert.Equal(1.0, result, precision: 6);
    }

    [Fact]
    public void Compute_ReturnsZero_ForOrthogonalVectors()
    {
        var a = new float[] { 1f, 0f };
        var b = new float[] { 0f, 1f };
        var result = CosineSimilarity.Compute(a, b);
        Assert.Equal(0.0, result, precision: 6);
    }

    [Fact]
    public void Compute_RanksMoreSimilarVectorHigher()
    {
        var query = new float[] { 1f, 1f, 0f };
        var close = new float[] { 1f, 0.9f, 0f };
        var far = new float[] { 0f, 0f, 1f };

        var closeSimilarity = CosineSimilarity.Compute(query, close);
        var farSimilarity = CosineSimilarity.Compute(query, far);

        Assert.True(closeSimilarity > farSimilarity);
    }

    [Fact]
    public void Compute_Throws_ForMismatchedLengths()
    {
        var a = new float[] { 1f, 2f };
        var b = new float[] { 1f, 2f, 3f };
        Assert.Throws<ArgumentException>(() => CosineSimilarity.Compute(a, b));
    }
}
