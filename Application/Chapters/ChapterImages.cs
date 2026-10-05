using Application.Covers;

namespace Application.Chapters;

/// <summary>
/// Pictures in chapters (#86): what <c>POST /api/novel/{novelId}/chapter-images</c> accepts and how it stores a picture.
/// The upload has the cover's checks (<see cref="NovelCovers"/>: JPEG, PNG or WebP by content, at most 5 MB, the same
/// refusal codes) without the cover's 2:3 rules: the whole picture is kept, turned upright, at most
/// <see cref="MaxLongSide"/> px on its long side, as one WebP file with no metadata. The address it answers is what a
/// chapter's <c>&lt;img src&gt;</c> holds (chapter format v1's image paragraph, #74).
/// <code>
/// chapter-images/{novelId}/{imageId}.webp
/// </code>
/// Every upload gets a new key, never overwritten. A novel's pictures share its folder (<see cref="NovelPrefix"/>), so
/// they can be found and removed by the novel.
/// </summary>
public static class ChapterImages
{
    public const string Folder = "chapter-images";

    /// <summary>The longest side a stored picture has; a larger one is reduced to it, keeping its aspect ratio.</summary>
    public const int MaxLongSide = 2000;

    /// <summary>The upload limit for the file itself, the cover's.</summary>
    public const long MaxUploadBytes = NovelCovers.MaxUploadBytes;

    public const string ContentType = "image/webp";

    /// <summary>The folder of all of a novel's chapter pictures.</summary>
    public static string NovelPrefix(Guid novelId) => $"{Folder}/{novelId:D}/";

    /// <summary>The key of one stored picture.</summary>
    public static string Key(Guid novelId, Guid imageId) => $"{NovelPrefix(novelId)}{imageId:N}.webp";

    /// <summary>
    /// The size a picture of <paramref name="width"/> x <paramref name="height"/> (upright) is stored at: as it is when
    /// its long side is at most <see cref="MaxLongSide"/> (never enlarged), else reduced so the long side is exactly
    /// that, the other side rounded and at least 1 px.
    /// </summary>
    public static (int Width, int Height) SizeFor(int width, int height)
    {
        var longSide = Math.Max(width, height);
        if (longSide <= MaxLongSide)
        {
            return (width, height);
        }

        var scale = (double)MaxLongSide / longSide;
        return width >= height
            ? (MaxLongSide, Math.Max(1, (int)Math.Round(height * scale)))
            : (Math.Max(1, (int)Math.Round(width * scale)), MaxLongSide);
    }

    /// <summary>
    /// The refusals that name the picture: the cover's sentences (<see cref="NovelCovers.Refusals"/>) with «الصورة»
    /// for «الغلاف».
    /// </summary>
    public static readonly ImageRefusalMessages Refusals = new(
        NotAnImage: "يجب أن تكون الصورة بصيغة JPEG أو PNG أو WebP.",
        FileTooLarge: $"يجب ألا يتجاوز حجم ملف الصورة {MaxUploadBytes / (1024 * 1024)} ميغابايت.");

    /// <summary>No file in <c>image</c> (400 ValidationFailed); the cover's message, for a picture.</summary>
    public static readonly string RequiredMessage =
        $"الصورة مطلوبة (JPEG أو PNG أو WebP، بحد أقصى {MaxUploadBytes / (1024 * 1024)} ميغابايت).";

    /// <summary>
    /// A declared type other than JPEG, PNG or WebP, or a file over the limit (400 ValidationFailed); the cover's message,
    /// for a picture.
    /// </summary>
    public static readonly string InvalidMessage =
        $"يجب أن تكون الصورة JPEG أو PNG أو WebP لا يتجاوز حجمها {MaxUploadBytes / (1024 * 1024)} ميغابايت.";

    /// <summary>The picture couldn't be processed or stored (400): as for a post's picture or a profile photo.</summary>
    public const string UploadFailedCode = "UploadFailed";

    public const string UploadFailedMessage = "تعذّر رفع الصورة، حاول مرة أخرى.";
}
