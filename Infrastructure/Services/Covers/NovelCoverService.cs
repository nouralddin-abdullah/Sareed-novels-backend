using Application.Covers;
using Application.Services;
using Microsoft.Extensions.Logging;

namespace Infrastructure.Services.Covers;

/// <summary>
/// Processes covers with <see cref="CoverImageProcessor"/> and stores every file of the result under one new folder
/// (<see cref="NovelCovers.KeyPrefix"/>). Files are never overwritten, so a changed cover always has a new URL.
/// </summary>
public sealed class NovelCoverService(
    IObjectStorage storage,
    IFileUploadService legacyUploads,
    ILogger<NovelCoverService> logger) : INovelCoverService
{
    /// <summary>Legacy covers up to this size are converted (the largest in production was 3.5 MB).</summary>
    public const long MaxExistingCoverBytes = 25 * 1024 * 1024;

    // One image at a time per process: decoding is the only large allocation in the API, and the host is small.
    private static readonly SemaphoreSlim OneAtATime = new(1, 1);

    private readonly CoverImageProcessor processor = new();

    public bool ProcessorAvailable => CoverImageProcessor.IsAvailable;

    public async Task<string> StoreUploadAsync(Guid novelId, Stream upload, CancellationToken cancellationToken = default)
    {
        var bytes = await ReadAllAsync(upload, NovelCovers.MaxUploadBytes, cancellationToken);

        if (!CoverImageProcessor.IsAvailable)
        {
            // Degraded mode, loudly: the native image library didn't load on this host. The cover is kept as uploaded
            // (after checking it really is an image) so authors aren't blocked, and the admin cover status and the
            // backfill show it as not converted. Fix the host, then run the backfill.
            logger.LogError(CoverImageProcessor.AvailabilityError,
                "Cover processing is unavailable; storing the cover for novel {NovelId} unprocessed", novelId);
            var contentType = SniffImageType(bytes)
                ?? throw new CoverImageException(CoverErrorCodes.UnsupportedFormat, "The cover must be a JPEG, PNG or WebP image.");
            using var original = new MemoryStream(bytes, writable: false);
            return await legacyUploads.UploadNovelImageAsync(original, contentType, novelId.ToString());
        }

        var cover = await ProcessAsync(bytes, enforceMinimumSize: true, cancellationToken);
        var url = await SaveAsync(novelId, cover, cancellationToken);
        logger.LogInformation("Stored cover for novel {NovelId}: {Layout} from {SourceWidth}x{SourceHeight} {Format}, {Bytes} bytes as {Url}",
            novelId, cover.Layout, cover.SourceWidth, cover.SourceHeight, cover.SourceFormat, bytes.Length, url);
        return url;
    }

    public async Task<CoverConversion> ConvertExistingAsync(Guid novelId, string coverUrl, bool dryRun, CancellationToken cancellationToken = default)
    {
        if (!CoverImageProcessor.IsAvailable)
            throw new InvalidOperationException("Cover processing is unavailable on this host.", CoverImageProcessor.AvailabilityError);

        if (storage.KeyOf(coverUrl) is null)
        {
            throw new CoverImageException(CoverErrorCodes.Unreadable,
                "The cover isn't in the configured bucket (CloudflareR2:PublicUrl), so it can't be read.");
        }

        byte[]? bytes;
        try
        {
            bytes = await storage.GetAsync(coverUrl, MaxExistingCoverBytes, cancellationToken);
        }
        catch (InvalidDataException ex)
        {
            throw new CoverImageException(CoverErrorCodes.FileTooLarge, ex.Message);
        }
        if (bytes is null)
        {
            throw new CoverImageException(CoverErrorCodes.Unreadable, "The cover file doesn't exist in the bucket.");
        }

        var cover = await ProcessAsync(bytes, enforceMinimumSize: false, cancellationToken);
        var outputBytes = cover.Files.Sum(f => (long)f.Bytes.Length);
        var url = dryRun ? null : await SaveAsync(novelId, cover, cancellationToken);
        return new CoverConversion(url, cover.Layout.ToString().ToLowerInvariant(), cover.SourceWidth, cover.SourceHeight,
            bytes.Length, cover.Width, outputBytes);
    }

    public async Task DeleteStandardCoverAsync(string coverUrl, CancellationToken cancellationToken = default)
    {
        var width = NovelCovers.FullWidthOf(coverUrl);
        var key = storage.KeyOf(coverUrl);
        if (width is null || key is null) return;

        var prefix = key[..key.LastIndexOf('/')];
        foreach (var w in NovelCovers.WidthsFor(width.Value))
        {
            await storage.DeleteAsync(NovelCovers.WebpKey(prefix, w), cancellationToken);
        }
        await storage.DeleteAsync(NovelCovers.JpegKey(prefix), cancellationToken);
        await storage.DeleteAsync(NovelCovers.ShareImageKey(prefix), cancellationToken);
    }

    private async Task<ProcessedCover> ProcessAsync(byte[] bytes, bool enforceMinimumSize, CancellationToken cancellationToken)
    {
        await OneAtATime.WaitAsync(cancellationToken);
        try
        {
            return processor.Process(bytes, enforceMinimumSize);
        }
        finally
        {
            OneAtATime.Release();
        }
    }

    /// <summary>Uploads the smaller files first, so the URL that gets saved only exists once everything it implies does.</summary>
    private async Task<string> SaveAsync(Guid novelId, ProcessedCover cover, CancellationToken cancellationToken)
    {
        var prefix = NovelCovers.KeyPrefix(novelId, Guid.NewGuid());
        string? fullUrl = null;
        foreach (var file in cover.Files.OrderBy(f => f == cover.Full))
        {
            using var content = new MemoryStream(file.Bytes, writable: false);
            var url = await storage.PutAsync($"{prefix}/{file.FileName}", content, file.ContentType, cancellationToken);
            if (file == cover.Full) fullUrl = url;
        }
        return fullUrl!;
    }

    private static async Task<byte[]> ReadAllAsync(Stream stream, long maxBytes, CancellationToken cancellationToken)
    {
        using var buffer = new MemoryStream();
        var chunk = new byte[81920];
        int read;
        while ((read = await stream.ReadAsync(chunk, cancellationToken)) > 0)
        {
            if (buffer.Length + read > maxBytes)
            {
                throw new CoverImageException(CoverErrorCodes.FileTooLarge, $"The cover file must be at most {maxBytes / (1024 * 1024)} MB.");
            }
            buffer.Write(chunk, 0, read);
        }
        return buffer.ToArray();
    }

    /// <summary>The image type from the file's first bytes (never from the client's declared type).</summary>
    internal static string? SniffImageType(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length >= 3 && bytes[0] == 0xFF && bytes[1] == 0xD8 && bytes[2] == 0xFF) return "image/jpeg";
        if (bytes.Length >= 8 && bytes[..8].SequenceEqual(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A })) return "image/png";
        if (bytes.Length >= 12 && bytes[..4].SequenceEqual("RIFF"u8) && bytes[8..12].SequenceEqual("WEBP"u8)) return "image/webp";
        return null;
    }
}
