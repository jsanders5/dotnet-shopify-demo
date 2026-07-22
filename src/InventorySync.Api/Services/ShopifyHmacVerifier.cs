using System.Security.Cryptography;
using System.Text;

namespace InventorySync.Api.Services;

public static class ShopifyHmacVerifier
{
    public static bool IsValid(string sharedSecret, byte[] requestBody, string? hmacHeader)
    {
        if (string.IsNullOrEmpty(hmacHeader)) return false;

        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(sharedSecret));
        var computedHash = hmac.ComputeHash(requestBody);

        return CryptographicOperations.FixedTimeEquals(computedHash, SafeBase64Decode(hmacHeader));
    }

    private static byte[] SafeBase64Decode(string value)
    {
        try
        {
            return Convert.FromBase64String(value);
        }
        catch (FormatException)
        {
            return Array.Empty<byte>();
        }
    }
}
