using System.Text.RegularExpressions;

namespace Application.Covers;

/// <summary>
/// Sard's cover standard. Every cover is a 2:3 portrait stored as a small set of files that share one folder:
/// <code>
/// novel-covers/{novelId}/{coverId}/320.webp
/// novel-covers/{novelId}/{coverId}/640.webp
/// novel-covers/{novelId}/{coverId}/960.webp   &lt;- Novel.CoverImageUrl (the largest width that was made)
/// novel-covers/{novelId}/{coverId}/cover.jpg  &lt;- 480 px JPEG for renderers without WebP
/// novel-covers/{novelId}/{coverId}/og.jpg     &lt;- 1200x630 share image (og:image) built from the cover
/// </code>
/// Clients get the smaller sizes from the stored URL: same folder, "{width}.webp" for each standard width below the
/// stored one (the web app's <c>src/utils/cover-image.js</c> does this for srcset). Widths above the source are never
/// made, so a small source is stored as, say, ".../320.webp" and ".../500.webp".
/// Anything else in Novel.CoverImageUrl is a legacy cover that the admin backfill hasn't converted yet.
/// </summary>
public static partial class NovelCovers
{
    /// <summary>Width:height of every cover.</summary>
    public const int RatioWidth = 2;
    public const int RatioHeight = 3;

    /// <summary>The widths that are made (when the source is at least that wide).</summary>
    public static readonly IReadOnlyList<int> StandardWidths = [320, 640, 960];

    public const int MaxWidth = 960;

    /// <summary>Uploads smaller than this (after cropping to 2:3) are refused: they look blurry even on small cards.</summary>
    public const int MinUploadWidth = 300;
    public const int MinUploadHeight = 450;

    public const int JpegWidth = 480;
    public const string JpegFileName = "cover.jpg";

    /// <summary>The share image for link previews (Facebook, WhatsApp, X...): the cover on a blurred copy of itself.</summary>
    public const string ShareImageFileName = "og.jpg";
    public const int ShareImageWidth = 1200;
    public const int ShareImageHeight = 630;

    public const string Folder = "novel-covers";

    /// <summary>Upload limit for the file itself (the web app shrinks photos before sending them).</summary>
    public const long MaxUploadBytes = 5 * 1024 * 1024;

    /// <summary>The height that goes with a cover width.</summary>
    public static int HeightFor(int width) => (int)Math.Round(width * (double)RatioHeight / RatioWidth);

    /// <summary>The folder of one stored cover.</summary>
    public static string KeyPrefix(Guid novelId, Guid coverId) => $"{Folder}/{novelId:D}/{coverId:N}";

    public static string WebpKey(string prefix, int width) => $"{prefix}/{width}.webp";

    public static string JpegKey(string prefix) => $"{prefix}/{JpegFileName}";

    public static string ShareImageKey(string prefix) => $"{prefix}/{ShareImageFileName}";

    /// <summary>The widths stored for a cover whose largest width is <paramref name="fullWidth"/>.</summary>
    public static IReadOnlyList<int> WidthsFor(int fullWidth) =>
        StandardWidths.Where(w => w < fullWidth).Append(fullWidth).ToList();

    /// <summary>True when the URL is a cover in this standard (as opposed to a legacy upload).</summary>
    public static bool IsStandard(string? url) => url is not null && StandardUrl().IsMatch(url);

    /// <summary>The largest width of a standard cover URL, or null for a legacy URL.</summary>
    public static int? FullWidthOf(string? url)
    {
        if (url is null) return null;
        var match = StandardUrl().Match(url);
        return match.Success ? int.Parse(match.Groups["width"].Value) : null;
    }

    /// <summary>The JPEG next to a standard cover, or null for a legacy URL.</summary>
    public static string? JpegUrlOf(string? url) => Sibling(url, JpegFileName);

    /// <summary>The share image next to a standard cover, or null for a legacy URL.</summary>
    public static string? ShareImageUrlOf(string? url) => Sibling(url, ShareImageFileName);

    private static string? Sibling(string? url, string fileName) =>
        IsStandard(url) ? url![..(url!.LastIndexOf('/') + 1)] + fileName : null;

    /// <summary>Marker used to find legacy covers in SQL: every standard cover URL contains it, no legacy one does.</summary>
    public const string StandardUrlMarker = "/" + Folder + "/";

    [GeneratedRegex(@"/novel-covers/[0-9a-fA-F-]{36}/[0-9a-f]{32}/(?<width>[1-9][0-9]{1,3})\.webp$")]
    private static partial Regex StandardUrl();
}
