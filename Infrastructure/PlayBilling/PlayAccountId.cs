using System.Security.Cryptography;
using System.Text;

namespace Infrastructure.PlayBilling;

/// <summary>
/// The obfuscatedAccountId the app gives Google Play when it launches a purchase: the lowercase hex SHA-256 of the
/// UTF-8 bytes of the Sard user id (the JWT's nameidentifier claim), 64 characters, which is Play's limit. Google
/// returns it on the purchase as obfuscatedExternalAccountId, which is how the server knows the purchase was made for
/// the account that claims it. It is one-way and holds no personal data (Play blocks purchases whose id looks like an
/// email or a name).
/// </summary>
public static class PlayAccountId
{
    public static string For(string userId) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(userId))).ToLowerInvariant();

    public static bool Matches(string? obfuscatedAccountId, string userId) =>
        obfuscatedAccountId is not null && string.Equals(obfuscatedAccountId, For(userId), StringComparison.OrdinalIgnoreCase);
}
