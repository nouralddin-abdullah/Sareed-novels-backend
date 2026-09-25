namespace Application.Services;

/// <summary>
/// Turns images into covers in the Sard standard (see <see cref="Application.Covers.NovelCovers"/>) and stores them.
/// </summary>
public interface INovelCoverService
{
    /// <summary>False when the image library can't run on this host (e.g. its native part failed to load).</summary>
    bool ProcessorAvailable { get; }

    /// <summary>
    /// Normalizes an uploaded cover (orientation, 2:3 crop, sizes, WebP, no metadata) and stores it. Returns the URL
    /// to save in Novel.CoverImageUrl. Throws <see cref="Application.Covers.CoverImageException"/> when the file
    /// can't be used as a cover.
    /// </summary>
    Task<string> StoreUploadAsync(Guid novelId, Stream upload, CancellationToken cancellationToken = default);

    /// <summary>
    /// Converts a cover that is already stored (a legacy upload) to the standard. With <paramref name="dryRun"/> the
    /// image is read and processed but nothing is written. Throws
    /// <see cref="Application.Covers.CoverImageException"/> when the existing image can't be used.
    /// </summary>
    Task<CoverConversion> ConvertExistingAsync(Guid novelId, string coverUrl, bool dryRun, CancellationToken cancellationToken = default);

    /// <summary>Removes every file of a standard cover (used when a conversion lost a race and its result is unused).</summary>
    Task DeleteStandardCoverAsync(string coverUrl, CancellationToken cancellationToken = default);
}

/// <summary>What a conversion did (or would do, in a dry run).</summary>
public record CoverConversion(
    string? NewUrl,
    string Mode,
    int SourceWidth,
    int SourceHeight,
    long SourceBytes,
    int OutputWidth,
    long OutputBytes);
