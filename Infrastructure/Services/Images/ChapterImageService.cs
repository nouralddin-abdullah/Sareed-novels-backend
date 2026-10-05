using Application.Chapters;
using Application.Services;
using Microsoft.Extensions.Logging;

namespace Infrastructure.Services.Images;

/// <summary>
/// Processes chapter pictures with <see cref="ChapterImageProcessor"/> and stores each as one new object under its
/// novel (<see cref="ChapterImages.Key"/>), never overwritten.
/// </summary>
public sealed class ChapterImageService(IObjectStorage storage, ILogger<ChapterImageService> logger) : IChapterImageService
{
    private readonly ChapterImageProcessor processor = new();

    public async Task<string> StoreUploadAsync(Guid novelId, Stream upload, CancellationToken cancellationToken = default)
    {
        var bytes = await ImagePipeline.ReadUploadAsync(upload, ChapterImages.MaxUploadBytes, ChapterImages.Refusals, cancellationToken);

        // Unlike a cover, a picture is never stored unprocessed: it would keep the photo's EXIF and GPS, and no backfill
        // converts chapter pictures later.
        if (!ImagePipeline.IsAvailable)
        {
            throw new InvalidOperationException("Image processing is unavailable on this host; chapter pictures can't be stored.",
                ImagePipeline.AvailabilityError);
        }

        var picture = await ImagePipeline.OneAtATimeAsync(() => processor.Process(bytes), cancellationToken);
        using var content = new MemoryStream(picture.Bytes, writable: false);
        var url = await storage.PutAsync(ChapterImages.Key(novelId, Guid.NewGuid()), content, ChapterImages.ContentType, cancellationToken);

        logger.LogInformation("Stored a chapter picture for novel {NovelId}: {Width}x{Height} from {SourceWidth}x{SourceHeight} {Format}, {Bytes} bytes as {Url}",
            novelId, picture.Width, picture.Height, picture.SourceWidth, picture.SourceHeight, picture.SourceFormat, picture.Bytes.Length, url);
        return url;
    }
}
