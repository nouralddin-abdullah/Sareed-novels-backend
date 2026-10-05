namespace Application.Covers;

/// <summary>
/// The uploaded file can't be used as a cover. <see cref="Code"/> is stable (the web app maps it to its own message);
/// <see cref="Exception.Message"/> is Arabic text for other API clients. Handlers turn this into a 400.
/// </summary>
public class CoverImageException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}

/// <summary>Error codes returned with a 400 when a cover is refused.</summary>
public static class CoverErrorCodes
{
    /// <summary>Not a JPEG, PNG or WebP image (by content, whatever the file name or declared type says).</summary>
    public const string UnsupportedFormat = "cover_unsupported_format";

    /// <summary>Looks like an image but can't be decoded (truncated or corrupt).</summary>
    public const string Unreadable = "cover_unreadable";

    /// <summary>Smaller than the minimum after cropping to 2:3.</summary>
    public const string TooSmall = "cover_too_small";

    /// <summary>Too many pixels to decode safely on the server.</summary>
    public const string TooManyPixels = "cover_too_many_pixels";

    /// <summary>The file is over the upload limit.</summary>
    public const string FileTooLarge = "cover_file_too_large";
}

/// <summary>
/// The refusal messages that name what is uploaded: «الغلاف» for a cover (<see cref="NovelCovers.Refusals"/>). The
/// other refusals (unreadable, too many pixels) speak of «الصورة» and are the same for every picture.
/// </summary>
/// <param name="NotAnImage">Not a JPEG, PNG or WebP image (<see cref="CoverErrorCodes.UnsupportedFormat"/>); a sentence ending in a full stop.</param>
/// <param name="FileTooLarge">The file is over the upload limit (<see cref="CoverErrorCodes.FileTooLarge"/>).</param>
public sealed record ImageRefusalMessages(string NotAnImage, string FileTooLarge)
{
    /// <summary><see cref="NotAnImage"/>, saying which format the file is in (a GIF, say).</summary>
    public string NotAnImageOf(string format) => $"{NotAnImage.TrimEnd('.')} (هذا الملف بصيغة {format}).";
}
