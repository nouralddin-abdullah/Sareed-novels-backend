namespace Application.Covers;

/// <summary>
/// The uploaded file can't be used as a cover. <see cref="Code"/> is stable (the web app maps it to a translated
/// message); <see cref="Exception.Message"/> is English text for API clients and logs. Handlers turn this into a 400.
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
