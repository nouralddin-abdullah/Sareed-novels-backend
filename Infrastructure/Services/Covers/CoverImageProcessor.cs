using Application.Covers;
using Infrastructure.Services.Images;
using SkiaSharp;

namespace Infrastructure.Services.Covers;

/// <summary>One file of a processed cover.</summary>
public sealed record CoverFile(string FileName, int Width, int Height, string ContentType, byte[] Bytes);

/// <summary>A cover in the Sard standard, ready to store.</summary>
public sealed record ProcessedCover(
    CoverLayout Layout,
    SKEncodedImageFormat SourceFormat,
    int SourceWidth,
    int SourceHeight,
    int Width,
    int Height,
    IReadOnlyList<CoverFile> Files)
{
    /// <summary>The largest WebP, whose URL is stored on the novel.</summary>
    public CoverFile Full => Files.Single(f => f.FileName == $"{Width}.webp");
}

/// <summary>
/// Turns an uploaded image into a cover in the Sard standard (<see cref="NovelCovers"/>): EXIF orientation applied,
/// converted to sRGB, solid letterbox/pillarbox bars removed, cropped or fitted to 2:3 (<see cref="CoverGeometry.Plan"/>),
/// transparency flattened onto white, written as WebP at the standard widths plus a small JPEG and the 1200x630 share
/// image (<see cref="ShareImageRenderer"/>), with no metadata (no EXIF, GPS or ICC) in any file.
/// </summary>
/// <remarks>
/// Opening, decoding, reducing and encoding are the steps chapter pictures share (<see cref="SourceImage"/>,
/// <see cref="ImagePipeline"/>), with their memory bounds: the decoded image is halved before the final resample, and
/// callers run one image at a time.
/// </remarks>
public sealed class CoverImageProcessor
{
    /// <summary>
    /// Processes one image. <paramref name="enforceMinimumSize"/> is on for uploads and off for the backfill of existing
    /// covers (which keeps even a small legacy cover rather than dropping it).
    /// </summary>
    /// <exception cref="CoverImageException">The bytes aren't a usable JPEG, PNG or WebP image.</exception>
    public ProcessedCover Process(byte[] bytes, bool enforceMinimumSize)
    {
        using var source = SourceImage.Open(bytes, NovelCovers.Refusals);
        var raw = source.Size;
        var origin = source.Origin;
        var upright = source.Upright;
        var plan = CoverGeometry.Plan(upright.Width, upright.Height);

        var decoded = source.Decode(plan.ContentScale);
        try
        {
            // Screenshots and pasted images often carry solid bars (letterbox/pillarbox); plan on the artwork inside them.
            var content = FindContent(decoded);
            if (content != new SKRectI(0, 0, decoded.Width, decoded.Height))
            {
                var inSource = ScaleRect(content, (double)raw.Width / decoded.Width, (double)raw.Height / decoded.Height, raw);
                var uprightContent = Round(ImageOrientation.Matrix(origin, raw.Width, raw.Height).MapRect(inSource), upright);
                var inner = CoverGeometry.Plan(uprightContent.Width, uprightContent.Height, allowance: CropAllowance.AfterTrim);
                var innerSource = inner.Source;
                innerSource.Offset(uprightContent.Left, uprightContent.Top);
                plan = inner with { Source = innerSource };

                // The bars are gone, so the artwork may need more detail than the first decode kept.
                if (plan.ContentScale > (double)decoded.Width / raw.Width * 1.05 && decoded.Width < raw.Width)
                {
                    decoded.Dispose();
                    decoded = source.Decode(plan.ContentScale);
                }
            }

            if (enforceMinimumSize && plan.Width < NovelCovers.MinUploadWidth)
            {
                throw new CoverImageException(CoverErrorCodes.TooSmall,
                    $"الغلاف صغير جداً ({plan.Source.Width}×{plan.Source.Height})؛ يلزم {NovelCovers.MinUploadWidth}×{NovelCovers.MinUploadHeight} بكسل على الأقل بنسبة 2:3.");
            }

            var reduced = ImagePipeline.HalveWhileLarge(decoded, raw, plan.ContentScale);
            try
            {
                using var master = Render(reduced, origin, upright, plan);
                var files = new List<CoverFile>();
                foreach (var width in NovelCovers.WidthsFor(plan.Width).OrderDescending())
                {
                    var height = NovelCovers.HeightFor(width);
                    using var image = width == plan.Width ? null : ImagePipeline.Resize(master, width, height);
                    files.Add(new CoverFile($"{width}.webp", width, height, "image/webp", ImagePipeline.EncodeWebp(image ?? master)));
                }

                var jpegWidth = Math.Min(NovelCovers.JpegWidth, plan.Width);
                var jpegHeight = NovelCovers.HeightFor(jpegWidth);
                using (var jpegImage = jpegWidth == plan.Width ? null : ImagePipeline.Resize(master, jpegWidth, jpegHeight))
                {
                    files.Add(new CoverFile(NovelCovers.JpegFileName, jpegWidth, jpegHeight, "image/jpeg", ImagePipeline.EncodeJpeg(jpegImage ?? master)));
                }

                using (var share = ShareImageRenderer.Render(master))
                {
                    files.Add(new CoverFile(NovelCovers.ShareImageFileName, share.Width, share.Height, "image/jpeg", ImagePipeline.EncodeJpeg(share)));
                }

                return new ProcessedCover(plan.Layout, source.Format, upright.Width, upright.Height, plan.Width, plan.Height, files);
            }
            finally
            {
                if (!ReferenceEquals(reduced, decoded)) reduced.Dispose();
            }
        }
        finally
        {
            decoded.Dispose();
        }
    }

    /// <summary>Draws the upright, cropped or fitted cover at its full size onto white.</summary>
    private static SKImage Render(SKImage image, SKEncodedOrigin origin, SKSizeI upright, CoverPlan plan) =>
        ImagePipeline.OnWhite(plan.Width, plan.Height, canvas =>
        {
            var orient = ImageOrientation.Matrix(origin, image.Width, image.Height);
            var reducedUpright = ImageOrientation.UprightSize(image.Width, image.Height, origin);
            var ux = (float)reducedUpright.Width / upright.Width;
            var uy = (float)reducedUpright.Height / upright.Height;

            if (plan.Layout == CoverLayout.Crop)
            {
                var source = new SKRect(plan.Source.Left * ux, plan.Source.Top * uy, plan.Source.Right * ux, plan.Source.Bottom * uy);
                ImagePipeline.DrawUpright(canvas, image, orient, source, new SKRect(0, 0, plan.Width, plan.Height), ImagePipeline.Final);
            }
            else
            {
                var whole = new SKRect(plan.Source.Left * ux, plan.Source.Top * uy, plan.Source.Right * ux, plan.Source.Bottom * uy);

                // Backdrop: the artwork scaled to fill the cover, blurred and darkened, so the fitted artwork sits on its own colours.
                var fill = Math.Max(plan.Width / whole.Width, plan.Height / whole.Height);
                var fillRect = Centered(whole.Width * fill, whole.Height * fill, plan.Width, plan.Height);
                using (var blur = new SKPaint { ImageFilter = SKImageFilter.CreateBlur(plan.Width / 20f, plan.Width / 20f, SKShaderTileMode.Clamp) })
                {
                    canvas.SaveLayer(blur);
                    ImagePipeline.DrawUpright(canvas, image, orient, whole, fillRect, ImagePipeline.Final);
                    canvas.Restore();
                }
                using (var shade = new SKPaint { Color = new SKColor(0, 0, 0, 96) })
                {
                    canvas.DrawRect(new SKRect(0, 0, plan.Width, plan.Height), shade);
                }

                var fit = Math.Min(plan.Width / whole.Width, plan.Height / whole.Height);
                ImagePipeline.DrawUpright(canvas, image, orient, whole, Centered(whole.Width * fit, whole.Height * fit, plan.Width, plan.Height), ImagePipeline.Final);
            }
        });

    /// <summary>
    /// Colour difference per channel still counted as "the same" bar colour. Pasted bars are flat (JPEG keeps them
    /// within a few levels); a dark or light background that is part of the artwork varies more than this.
    /// </summary>
    private const int BarTolerance = 8;

    /// <summary>
    /// The part of the image inside solid bars, or the whole image. Bars are only removed in matching pairs (left and
    /// right, or top and bottom) of about the same thickness and colour, each at least 2% of the side and leaving at
    /// least 30% of it, so a cover with a plain sky or margin on one side is never trimmed.
    /// </summary>
    internal static SKRectI FindContent(SKImage image)
    {
        using var pixmap = image.PeekPixels();
        if (pixmap is null || pixmap.ColorType != SKColorType.Rgba8888) return new SKRectI(0, 0, image.Width, image.Height);
        return FindContent(pixmap.GetPixelSpan(), image.Width, image.Height, pixmap.RowBytes);
    }

    internal static SKRectI FindContent(ReadOnlySpan<byte> rgba, int width, int height, int rowBytes)
    {
        var left = 0;
        var right = width;
        var top = 0;
        var bottom = height;

        // Pillarbox (left and right bars) first, then letterbox within what is left.
        var l = BarLength(rgba, rowBytes, width, 0, height, alongX: true, fromStart: true, out var leftColour);
        var r = BarLength(rgba, rowBytes, width, 0, height, alongX: true, fromStart: false, out var rightColour);
        if (IsBarPair(l, r, width, leftColour, rightColour))
        {
            left = l;
            right = width - r;
        }

        var t = BarLength(rgba, rowBytes, height, left, right, alongX: false, fromStart: true, out var topColour);
        var b = BarLength(rgba, rowBytes, height, left, right, alongX: false, fromStart: false, out var bottomColour);
        if (IsBarPair(t, b, height, topColour, bottomColour))
        {
            top = t;
            bottom = height - b;
        }

        return new SKRectI(left, top, right, bottom);
    }

    private static bool IsBarPair(int first, int second, int side, uint firstColour, uint secondColour)
    {
        var min = Math.Max(2, side / 50);
        return first >= min && second >= min
            && Math.Abs(first - second) <= Math.Max(0.25 * Math.Max(first, second), side * 0.03)
            && side - first - second >= side * 0.3
            && SameColour(firstColour, secondColour);
    }

    /// <summary>
    /// How many whole lines (columns when <paramref name="alongX"/>, else rows) from one edge are a single colour, over
    /// the span <paramref name="spanStart"/>..<paramref name="spanEnd"/> of the other axis. 99% of a line's pixels
    /// must match, so a stray noisy pixel doesn't end the bar.
    /// </summary>
    private static int BarLength(ReadOnlySpan<byte> rgba, int rowBytes, int lines, int spanStart, int spanEnd, bool alongX, bool fromStart, out uint colour)
    {
        colour = 0;
        if (spanEnd - spanStart < 1 || lines < 1) return 0;

        var firstLine = fromStart ? 0 : lines - 1;
        var middle = (spanStart + spanEnd) / 2;
        colour = alongX ? Pixel(rgba, rowBytes, firstLine, middle) : Pixel(rgba, rowBytes, middle, firstLine);

        var allowedMisses = (spanEnd - spanStart) / 100;
        for (var n = 0; n < lines; n++)
        {
            var line = fromStart ? n : lines - 1 - n;
            var misses = 0;
            for (var i = spanStart; i < spanEnd && misses <= allowedMisses; i++)
            {
                var pixel = alongX ? Pixel(rgba, rowBytes, line, i) : Pixel(rgba, rowBytes, i, line);
                if (!SameColour(pixel, colour)) misses++;
            }
            if (misses > allowedMisses) return n;
        }
        return lines;
    }

    private static uint Pixel(ReadOnlySpan<byte> rgba, int rowBytes, int x, int y)
    {
        var i = y * rowBytes + x * 4;
        return (uint)(rgba[i] | rgba[i + 1] << 8 | rgba[i + 2] << 16);
    }

    private static bool SameColour(uint a, uint b) =>
        Math.Abs((int)(a & 0xFF) - (int)(b & 0xFF)) <= BarTolerance
        && Math.Abs((int)(a >> 8 & 0xFF) - (int)(b >> 8 & 0xFF)) <= BarTolerance
        && Math.Abs((int)(a >> 16 & 0xFF) - (int)(b >> 16 & 0xFF)) <= BarTolerance;

    private static SKRect ScaleRect(SKRectI rect, double sx, double sy, SKSizeI bounds) => new(
        (float)Math.Max(0, rect.Left * sx), (float)Math.Max(0, rect.Top * sy),
        (float)Math.Min(bounds.Width, rect.Right * sx), (float)Math.Min(bounds.Height, rect.Bottom * sy));

    private static SKRectI Round(SKRect rect, SKSizeI bounds) => new(
        Math.Max(0, (int)Math.Round(rect.Left)), Math.Max(0, (int)Math.Round(rect.Top)),
        Math.Min(bounds.Width, (int)Math.Round(rect.Right)), Math.Min(bounds.Height, (int)Math.Round(rect.Bottom)));

    private static SKRect Centered(float width, float height, int boxWidth, int boxHeight)
    {
        var left = (boxWidth - width) / 2;
        var top = (boxHeight - height) / 2;
        return new SKRect(left, top, left + width, top + height);
    }
    /// <summary>The share image for novels without a usable cover (shipped by the web app as public/og-default.jpg).</summary>
    public static byte[] DefaultShareImage()
    {
        using var image = ShareImageRenderer.RenderDefault();
        return ImagePipeline.EncodeJpeg(image);
    }
}
