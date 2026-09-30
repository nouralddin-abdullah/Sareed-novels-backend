using Application.Common;
using Application.Validation;

namespace Application.Posts;

/// <summary>
/// What a new post may hold (#43): text, a picture and a novel, at least one of them. <see cref="Commands.CreatePost.CreatePostCommandValidator"/>
/// checks these; GET /api/app/config serves the limits (posts.*) so the apps' counter and picture checks mirror them.
/// </summary>
public static class PostRules
{
    /// <summary>
    /// The longest text, in user-perceived characters (<see cref="TextElements"/>, as gift messages count them): an
    /// emoji, a flag or a letter with its tashkeel is one.
    /// </summary>
    public const int ContentMaxLength = 5000;

    /// <summary>
    /// The most UTF-16 units the text may take, whatever its length in characters: 20 per character, the room gift
    /// messages have (4000 for 200). Any real text fits (the longest emoji, a kiss with two skin tones, is 15 units); it
    /// stops a letter under thousands of combining marks, which counts as one character, from making a post of megabytes.
    /// </summary>
    public const int ContentMaxUtf16Length = 20 * ContentMaxLength;

    /// <summary>The largest picture, in bytes: 5 MB (5,242,880), as every other picture members upload.</summary>
    public const int ImageMaxBytes = ImageValidationUtils.MaxFileSizeBytes;

    /// <summary>
    /// The picture types, the Content-Type of the file as the apps should send it. The alias image/jpg is accepted as
    /// well (<see cref="ImageValidationUtils.AllowedImageTypes"/>), but not published.
    /// </summary>
    public static readonly IReadOnlyList<string> ImageTypes = ["image/jpeg", "image/png", "image/webp"];

    public const string ContentRequiredCode = "PostContentRequired";
    public const string ContentTooLongCode = "PostContentTooLong";
    public const string ImageTypeCode = "PostImageType";
    public const string ImageTooLargeCode = "PostImageTooLarge";
    public const string UploadFailedCode = "UploadFailed";

    public const string ContentRequiredMessage = "المنشور فارغ: اكتب نصًا أو أرفق صورة أو رواية.";
    public static readonly string ContentTooLongMessage = $"المنشور طويل: الحد الأقصى {ArabicCount.Letters(ContentMaxLength)}.";
    public const string ImageTypeMessage = "صيغة الصورة غير مدعومة: اختر صورة JPEG أو PNG أو WebP.";
    public static readonly string ImageTooLargeMessage = $"الصورة كبيرة: الحد الأقصى {ImageMaxBytes / (1024 * 1024)} ميغابايت.";
    public const string UploadFailedMessage = "تعذّر رفع الصورة، حاول مرة أخرى.";

    /// <summary>The text as it is kept: trimmed, and null when empty or only whitespace (a post without text).</summary>
    public static string? Normalize(string? content) => string.IsNullOrWhiteSpace(content) ? null : content.Trim();

    /// <summary>
    /// Whether a (<see cref="Normalize"/>d) text is over the limit: more than <see cref="ContentMaxLength"/> characters,
    /// or more than <see cref="ContentMaxUtf16Length"/> UTF-16 units.
    /// </summary>
    public static bool IsTooLong(string content) =>
        content.Length > ContentMaxUtf16Length || TextElements.Count(content) > ContentMaxLength;
}
