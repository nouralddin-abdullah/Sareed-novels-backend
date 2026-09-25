namespace Infrastructure.Services.Storage;

/// <summary>
/// Object keys for user uploads. Every upload gets a new key "{folder}/{ownerId}/{random}{ext}" that is never
/// overwritten, and the caller stores the returned URL.
/// </summary>
/// <remarks>
/// Keys used to be derived from editable text or reused per entity ("novel-images/{title}",
/// "profile-images/{userName}", "gift-images/{id}"). That broke in production: renamed novels kept showing their old
/// cover because the new one went to another key while the stored URL stayed put, novels with the same title
/// overwrote each other's cover, a title with a trailing space produced a URL browsers and apps resolve differently,
/// and every in-place overwrite left browsers, the CDN and the app's disk cache serving the old image.
/// </remarks>
public static class StorageKeys
{
    private static readonly Dictionary<string, string> Extensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ["image/jpeg"] = ".jpg",
        ["image/jpg"] = ".jpg",
        ["image/png"] = ".png",
        ["image/webp"] = ".webp",
        ["image/gif"] = ".gif",
        ["image/avif"] = ".avif",
        ["image/svg+xml"] = ".svg",
        ["application/pdf"] = ".pdf",
    };

    /// <summary>A fresh, unique key for one upload owned by <paramref name="ownerId"/> (an entity or user id).</summary>
    public static string NewKey(string folder, string ownerId, string? contentType)
    {
        if (string.IsNullOrWhiteSpace(ownerId) || ownerId.Contains('/'))
            throw new ArgumentException("Owner id must be a non-empty id without slashes.", nameof(ownerId));

        var extension = contentType is not null && Extensions.TryGetValue(contentType.Split(';')[0].Trim(), out var ext)
            ? ext
            : string.Empty;
        return $"{folder}/{ownerId.Trim()}/{Guid.NewGuid():N}{extension}";
    }

    /// <summary>The public URL for a key, with every path segment percent-encoded so it is unambiguous everywhere.</summary>
    public static string PublicUrl(string publicBaseUrl, string key) =>
        $"{publicBaseUrl.TrimEnd('/')}/{string.Join('/', key.Split('/').Select(Uri.EscapeDataString))}";

    /// <summary>The key behind a public URL (or the key itself), or null when the URL belongs to another host.</summary>
    public static string? KeyFrom(string publicBaseUrl, string keyOrUrl)
    {
        var prefix = publicBaseUrl.TrimEnd('/') + "/";
        if (!keyOrUrl.Contains("://")) return keyOrUrl;
        return keyOrUrl.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
            ? Uri.UnescapeDataString(keyOrUrl[prefix.Length..])
            : null;
    }
}
