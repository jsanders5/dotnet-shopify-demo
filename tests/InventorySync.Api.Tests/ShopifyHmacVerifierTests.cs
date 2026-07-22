using System.Security.Cryptography;
using System.Text;
using InventorySync.Api.Services;
using Xunit;

namespace InventorySync.Api.Tests;

public class ShopifyHmacVerifierTests
{
    private const string Secret = "test-shared-secret";

    [Fact]
    public void IsValid_ReturnsTrue_ForCorrectSignature()
    {
        var body = Encoding.UTF8.GetBytes("{\"available\":5}");
        var expectedHmac = ComputeHmac(Secret, body);

        Assert.True(ShopifyHmacVerifier.IsValid(Secret, body, expectedHmac));
    }

    [Fact]
    public void IsValid_ReturnsFalse_ForTamperedBody()
    {
        var originalBody = Encoding.UTF8.GetBytes("{\"available\":5}");
        var hmacForOriginal = ComputeHmac(Secret, originalBody);
        var tamperedBody = Encoding.UTF8.GetBytes("{\"available\":999}");

        Assert.False(ShopifyHmacVerifier.IsValid(Secret, tamperedBody, hmacForOriginal));
    }

    [Fact]
    public void IsValid_ReturnsFalse_ForMissingHeader()
    {
        var body = Encoding.UTF8.GetBytes("{\"available\":5}");

        Assert.False(ShopifyHmacVerifier.IsValid(Secret, body, null));
    }

    private static string ComputeHmac(string secret, byte[] body)
    {
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        return Convert.ToBase64String(hmac.ComputeHash(body));
    }
}
