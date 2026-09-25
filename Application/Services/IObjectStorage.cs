namespace Application.Services;

/// <summary>Plain object access to the public file bucket, for callers that choose their own keys.</summary>
public interface IObjectStorage
{
    /// <summary>Writes an object with a long immutable cache lifetime and returns its public URL.</summary>
    Task<string> PutAsync(string key, Stream content, string contentType, CancellationToken cancellationToken = default);

    /// <summary>
    /// Reads an object by key or public URL. Returns null when the URL isn't in this bucket or the object doesn't
    /// exist; throws <see cref="InvalidDataException"/> when it is larger than <paramref name="maxBytes"/>.
    /// </summary>
    Task<byte[]?> GetAsync(string keyOrUrl, long maxBytes, CancellationToken cancellationToken = default);

    Task DeleteAsync(string key, CancellationToken cancellationToken = default);

    /// <summary>The key behind a public URL of this bucket, or null.</summary>
    string? KeyOf(string keyOrUrl);
}
