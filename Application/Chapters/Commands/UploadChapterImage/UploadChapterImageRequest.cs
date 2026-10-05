using Microsoft.AspNetCore.Http;

namespace Application.Chapters.Commands.UploadChapterImage;

/// <summary>The multipart form of POST /api/novel/{novelId}/chapter-images: one file, in <c>image</c>.</summary>
public class UploadChapterImageRequest
{
    /// <summary>
    /// The picture. Nullable, so that a form without it is answered by <see cref="UploadChapterImageRequestValidator"/>'s
    /// Arabic message rather than ASP.NET's own.
    /// </summary>
    public IFormFile? Image { get; init; }
}
