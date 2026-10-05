namespace Application.Services;

/// <summary>Turns uploads into chapter pictures (see <see cref="Application.Chapters.ChapterImages"/>) and stores them.</summary>
public interface IChapterImageService
{
    /// <summary>
    /// Normalizes an uploaded picture (orientation, at most 2000 px on the long side, WebP, no metadata) and stores it
    /// under the novel. Returns its public URL, for a chapter's <c>&lt;img src&gt;</c>. Throws
    /// <see cref="Application.Covers.CoverImageException"/> when the file can't be used as a picture; anything else it
    /// throws is a failure to process or store it.
    /// </summary>
    Task<string> StoreUploadAsync(Guid novelId, Stream upload, CancellationToken cancellationToken = default);
}
