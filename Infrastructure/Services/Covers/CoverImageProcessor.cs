using Application.Covers;
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
/// Memory is bounded for a small shared host: the source's pixel count is capped, JPEG and WebP sources are decoded at
/// a reduced scale when the cover needs fewer pixels, the decoded image is halved before the final resample, and callers
/// run one image at a time. The largest decode allowed is <see cref="MaxDecodedPixels"/> (64 MB of RGBA).
/// </remarks>
public sealed class CoverImageProcessor
{
    /// <summary>Sources with more pixels are refused before anything is decoded (e.g. a PNG "decompression bomb").</summary>
    public const long MaxSourcePixels = 50_000_000;

    /// <summary>Most pixels ever decoded at once. PNG can't be decoded at a reduced scale, so a larger PNG is refused.</summary>
    public const long MaxDecodedPixels = 16_777_216;

    public const int WebpQuality = 80;
    public const int JpegQuality = 85;

    private static readonly SKSamplingOptions Halving = new(SKFilterMode.Linear, SKMipmapMode.None);
    private static readonly SKSamplingOptions Final = new(SKCubicResampler.Mitchell);

    private static readonly Lazy<Exception?> LoadFailure = new(ProbeNativeLibrary);

    /// <summary>False when the native SkiaSharp library can't be loaded or used on this machine.</summary>
    public static bool IsAvailable => LoadFailure.Value is null;

    /// <summary>Why <see cref="IsAvailable"/> is false (null when it is true).</summary>
    public static Exception? AvailabilityError => LoadFailure.Value;

    /// <summary>
    /// Processes one image. <paramref name="enforceMinimumSize"/> is on for uploads and off for the backfill of existing
    /// covers (which keeps even a small legacy cover rather than dropping it).
    /// </summary>
    /// <exception cref="CoverImageException">The bytes aren't a usable JPEG, PNG or WebP image.</exception>
    public ProcessedCover Process(byte[] bytes, bool enforceMinimumSize)
    {
        using var data = SKData.CreateCopy(bytes);
        using var codec = SKCodec.Create(data)
            ?? throw new CoverImageException(CoverErrorCodes.UnsupportedFormat, "يجب أن يكون الغلاف صورة بصيغة JPEG أو PNG أو WebP.");

        var format = codec.EncodedFormat;
        if (format is not (SKEncodedImageFormat.Jpeg or SKEncodedImageFormat.Png or SKEncodedImageFormat.Webp))
        {
            throw new CoverImageException(CoverErrorCodes.UnsupportedFormat, $"يجب أن يكون الغلاف صورة بصيغة JPEG أو PNG أو WebP (هذا الملف بصيغة {format}).");
        }

        var raw = codec.Info.Size;
        if (raw.Width <= 0 || raw.Height <= 0)
        {
            throw new CoverImageException(CoverErrorCodes.Unreadable, "الصورة فارغة.");
        }
        if ((long)raw.Width * raw.Height > MaxSourcePixels)
        {
            throw new CoverImageException(CoverErrorCodes.TooManyPixels, $"أبعاد الصورة كبيرة جداً ({raw.Width}×{raw.Height})؛ استخدم صورة أقل من {MaxSourcePixels / 1_000_000} ميغابكسل.");
        }

        var origin = codec.EncodedOrigin;
        var upright = CoverGeometry.UprightSize(raw.Width, raw.Height, origin);
        var plan = CoverGeometry.Plan(upright.Width, upright.Height);

        var decoded = Decode(codec, raw, plan.ContentScale);
        try
        {
            // Screenshots and pasted images often carry solid bars (letterbox/pillarbox); plan on the artwork inside them.
            var content = FindContent(decoded);
            if (content != new SKRectI(0, 0, decoded.Width, decoded.Height))
            {
                var inSource = ScaleRect(content, (double)raw.Width / decoded.Width, (double)raw.Height / decoded.Height, raw);
                var uprightContent = Round(CoverGeometry.OrientationMatrix(origin, raw.Width, raw.Height).MapRect(inSource), upright);
                var inner = CoverGeometry.Plan(uprightContent.Width, uprightContent.Height, allowance: CropAllowance.AfterTrim);
                var source = inner.Source;
                source.Offset(uprightContent.Left, uprightContent.Top);
                plan = inner with { Source = source };

                // The bars are gone, so the artwork may need more detail than the first decode kept.
                if (plan.ContentScale > (double)decoded.Width / raw.Width * 1.05 && decoded.Width < raw.Width)
                {
                    decoded.Dispose();
                    decoded = Decode(codec, raw, plan.ContentScale);
                }
            }

            if (enforceMinimumSize && plan.Width < NovelCovers.MinUploadWidth)
            {
                throw new CoverImageException(CoverErrorCodes.TooSmall,
                    $"الغلاف صغير جداً ({plan.Source.Width}×{plan.Source.Height})؛ يلزم {NovelCovers.MinUploadWidth}×{NovelCovers.MinUploadHeight} بكسل على الأقل بنسبة 2:3.");
            }

            var reduced = HalveWhileLarge(decoded, raw, plan.ContentScale);
            try
            {
                using var master = Render(reduced, origin, upright, plan);
                var files = new List<CoverFile>();
                foreach (var width in NovelCovers.WidthsFor(plan.Width).OrderDescending())
                {
                    var height = NovelCovers.HeightFor(width);
                    using var image = width == plan.Width ? null : Resize(master, width, height);
                    files.Add(new CoverFile($"{width}.webp", width, height, "image/webp", EncodeWebp(image ?? master)));
                }

                var jpegWidth = Math.Min(NovelCovers.JpegWidth, plan.Width);
                var jpegHeight = NovelCovers.HeightFor(jpegWidth);
                using (var jpegImage = jpegWidth == plan.Width ? null : Resize(master, jpegWidth, jpegHeight))
                {
                    files.Add(new CoverFile(NovelCovers.JpegFileName, jpegWidth, jpegHeight, "image/jpeg", EncodeJpeg(jpegImage ?? master)));
                }

                using (var share = ShareImageRenderer.Render(master))
                {
                    files.Add(new CoverFile(NovelCovers.ShareImageFileName, share.Width, share.Height, "image/jpeg", EncodeJpeg(share)));
                }

                return new ProcessedCover(plan.Layout, format, upright.Width, upright.Height, plan.Width, plan.Height, files);
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

    /// <summary>Decodes to sRGB, at the smallest JPEG/WebP scale (n/8) that keeps <paramref name="neededScale"/> of the detail.</summary>
    private static SKImage Decode(SKCodec codec, SKSizeI raw, double neededScale)
    {
        var size = raw;
        if (neededScale < 1 && codec.EncodedFormat is SKEncodedImageFormat.Jpeg or SKEncodedImageFormat.Webp)
        {
            for (var eighths = 1; eighths < 8; eighths++)
            {
                var candidate = codec.GetScaledDimensions(eighths / 8f);
                if (candidate.Width >= raw.Width * neededScale && candidate.Height >= raw.Height * neededScale)
                {
                    size = candidate;
                    break;
                }
            }
        }

        if ((long)size.Width * size.Height > MaxDecodedPixels)
        {
            throw new CoverImageException(CoverErrorCodes.TooManyPixels,
                $"أبعاد الصورة كبيرة جداً للمعالجة ({raw.Width}×{raw.Height})؛ صغّرها أو استخدم صيغة JPEG.");
        }

        var info = new SKImageInfo(size.Width, size.Height, SKColorType.Rgba8888, SKAlphaType.Premul, SKColorSpace.CreateSrgb());
        using var bitmap = new SKBitmap();
        if (!bitmap.TryAllocPixels(info))
        {
            throw new CoverImageException(CoverErrorCodes.TooManyPixels, "الصورة كبيرة جداً للمعالجة الآن.");
        }

        var result = codec.GetPixels(info, bitmap.GetPixels());
        if (result != SKCodecResult.Success)
        {
            throw new CoverImageException(CoverErrorCodes.Unreadable, $"تعذّرت قراءة الصورة ({result})؛ قد يكون الملف تالفاً أو ناقصاً.");
        }

        bitmap.SetImmutable();
        return SKImage.FromBitmap(bitmap)
            ?? throw new CoverImageException(CoverErrorCodes.Unreadable, "تعذّرت قراءة الصورة.");
    }

    /// <summary>
    /// Halves the decoded image while the cover needs at most half of its detail: repeated 2:1 box reductions keep fine
    /// detail (text on covers) from aliasing, and the last resample is always by less than 2x.
    /// </summary>
    private static SKImage HalveWhileLarge(SKImage decoded, SKSizeI raw, double neededScale)
    {
        var current = decoded;
        while (true)
        {
            var scaleSoFar = (double)current.Width / raw.Width;
            if (neededScale / scaleSoFar > 0.5 || current.Width < 4 || current.Height < 4) return current;

            var next = Draw(current, current.Width / 2, current.Height / 2, Halving, current.ColorSpace);
            if (!ReferenceEquals(current, decoded)) current.Dispose();
            current = next;
        }
    }

    /// <summary>Draws the upright, cropped or fitted cover at its full size onto white.</summary>
    private static SKImage Render(SKImage image, SKEncodedOrigin origin, SKSizeI upright, CoverPlan plan)
    {
        // Colour space left null on purpose: pixels are already sRGB, and encoders then add no ICC profile.
        using var surface = SKSurface.Create(new SKImageInfo(plan.Width, plan.Height, SKColorType.Rgba8888, SKAlphaType.Opaque))
            ?? throw new InvalidOperationException("Could not allocate the cover surface.");
        var canvas = surface.Canvas;
        canvas.Clear(SKColors.White);

        var orient = CoverGeometry.OrientationMatrix(origin, image.Width, image.Height);
        var reducedUpright = CoverGeometry.UprightSize(image.Width, image.Height, origin);
        var ux = (float)reducedUpright.Width / upright.Width;
        var uy = (float)reducedUpright.Height / upright.Height;

        if (plan.Layout == CoverLayout.Crop)
        {
            var source = new SKRect(plan.Source.Left * ux, plan.Source.Top * uy, plan.Source.Right * ux, plan.Source.Bottom * uy);
            DrawUpright(canvas, image, orient, source, new SKRect(0, 0, plan.Width, plan.Height), null);
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
                DrawUpright(canvas, image, orient, whole, fillRect, null);
                canvas.Restore();
            }
            using (var shade = new SKPaint { Color = new SKColor(0, 0, 0, 96) })
            {
                canvas.DrawRect(new SKRect(0, 0, plan.Width, plan.Height), shade);
            }

            var fit = Math.Min(plan.Width / whole.Width, plan.Height / whole.Height);
            DrawUpright(canvas, image, orient, whole, Centered(whole.Width * fit, whole.Height * fit, plan.Width, plan.Height), null);
        }

        canvas.Flush();
        return surface.Snapshot();
    }

    /// <summary>Draws the part <paramref name="source"/> (in upright coordinates) of a stored image into <paramref name="destination"/>.</summary>
    private static void DrawUpright(SKCanvas canvas, SKImage image, SKMatrix orient, SKRect source, SKRect destination, SKPaint? paint)
    {
        var scaleX = destination.Width / source.Width;
        var scaleY = destination.Height / source.Height;
        var place = SKMatrix.CreateScaleTranslation(scaleX, scaleY, destination.Left - source.Left * scaleX, destination.Top - source.Top * scaleY);

        canvas.Save();
        canvas.ClipRect(destination);
        canvas.SetMatrix(SKMatrix.Concat(place, orient));
        canvas.DrawImage(image, 0, 0, Final, paint);
        canvas.Restore();
    }

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

    /// <summary>High-quality downscale: halve while more than 2x too large, then one bicubic step.</summary>
    internal static SKImage Resize(SKImage source, int width, int height)
    {
        var current = source;
        while (current.Width / 2 >= width && current.Height / 2 >= height)
        {
            var next = Draw(current, current.Width / 2, current.Height / 2, Halving, current.ColorSpace);
            if (!ReferenceEquals(current, source)) current.Dispose();
            current = next;
        }

        var result = Draw(current, width, height, Final, current.ColorSpace);
        if (!ReferenceEquals(current, source)) current.Dispose();
        return result;
    }

    private static SKImage Draw(SKImage source, int width, int height, SKSamplingOptions sampling, SKColorSpace? colorSpace)
    {
        var alpha = source.AlphaType == SKAlphaType.Opaque ? SKAlphaType.Opaque : SKAlphaType.Premul;
        using var surface = SKSurface.Create(new SKImageInfo(width, height, SKColorType.Rgba8888, alpha, colorSpace))
            ?? throw new InvalidOperationException($"Could not allocate a {width}x{height} surface.");
        surface.Canvas.DrawImage(source, new SKRect(0, 0, source.Width, source.Height), new SKRect(0, 0, width, height), sampling);
        surface.Canvas.Flush();
        return surface.Snapshot();
    }

    private static byte[] EncodeWebp(SKImage image)
    {
        using var pixmap = image.PeekPixels() ?? throw new InvalidOperationException("Cover pixels are not readable.");
        using var encoded = pixmap.Encode(new SKWebpEncoderOptions(SKWebpEncoderCompression.Lossy, WebpQuality))
            ?? throw new InvalidOperationException("WebP encoding failed.");
        return encoded.ToArray();
    }

    /// <summary>The share image for novels without a usable cover (shipped by the web app as public/og-default.jpg).</summary>
    public static byte[] DefaultShareImage()
    {
        using var image = ShareImageRenderer.RenderDefault();
        return EncodeJpeg(image);
    }

    private static byte[] EncodeJpeg(SKImage image)
    {
        using var pixmap = image.PeekPixels() ?? throw new InvalidOperationException("Cover pixels are not readable.");
        using var encoded = pixmap.Encode(new SKJpegEncoderOptions(JpegQuality, SKJpegEncoderDownsample.Downsample420, SKJpegEncoderAlphaOption.Ignore))
            ?? throw new InvalidOperationException("JPEG encoding failed.");
        return encoded.ToArray();
    }

    private static Exception? ProbeNativeLibrary()
    {
        try
        {
            using var surface = SKSurface.Create(new SKImageInfo(2, 3, SKColorType.Rgba8888, SKAlphaType.Opaque));
            if (surface is null) return new InvalidOperationException("SkiaSharp could not create a surface.");
            surface.Canvas.Clear(SKColors.White);
            using var image = surface.Snapshot();
            return EncodeWebp(image).Length > 0 ? null : new InvalidOperationException("SkiaSharp produced an empty WebP.");
        }
        catch (Exception ex)
        {
            // DllNotFoundException / TypeInitializationException when the native library is missing for this platform.
            return ex;
        }
    }
}
