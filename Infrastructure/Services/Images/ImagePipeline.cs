using Application.Covers;
using SkiaSharp;

namespace Infrastructure.Services.Images;

/// <summary>
/// The image steps covers (<see cref="Covers.CoverImageProcessor"/>) and chapter pictures
/// (<see cref="ChapterImageProcessor"/>) share after <see cref="SourceImage"/>: reducing with good
/// quality, drawing turned upright, flattening onto white, and encoding with no metadata (no EXIF, GPS or ICC); with
/// reading an upload within its limit, the one-image-at-a-time gate, and whether the native library works here.
/// </summary>
public static class ImagePipeline
{
    public const int WebpQuality = 80;
    public const int JpegQuality = 85;

    private static readonly SKSamplingOptions Halving = new(SKFilterMode.Linear, SKMipmapMode.None);

    /// <summary>The last step of a reduction: bicubic (Mitchell).</summary>
    public static readonly SKSamplingOptions Final = new(SKCubicResampler.Mitchell);

    /// <summary>
    /// Drawing at the same size (turned upright at most): each pixel copied as it is. Mitchell doesn't interpolate, so
    /// at 1:1 it would soften the picture.
    /// </summary>
    public static readonly SKSamplingOptions Exact = new(SKFilterMode.Nearest);

    // One image at a time per process, covers and chapter pictures together: decoding is the only large allocation in
    // the API, and the host is small.
    private static readonly SemaphoreSlim OneAtATime = new(1, 1);

    private static readonly Lazy<Exception?> LoadFailure = new(ProbeNativeLibrary);

    /// <summary>False when the native SkiaSharp library can't be loaded or used on this machine.</summary>
    public static bool IsAvailable => LoadFailure.Value is null;

    /// <summary>Why <see cref="IsAvailable"/> is false (null when it is true).</summary>
    public static Exception? AvailabilityError => LoadFailure.Value;

    /// <summary>Runs <paramref name="process"/> once no other image is being processed in this process.</summary>
    public static async Task<T> OneAtATimeAsync<T>(Func<T> process, CancellationToken cancellationToken)
    {
        await OneAtATime.WaitAsync(cancellationToken);
        try
        {
            return process();
        }
        finally
        {
            OneAtATime.Release();
        }
    }

    /// <summary>Reads an upload into memory, refusing it as soon as it is over <paramref name="maxBytes"/>.</summary>
    /// <exception cref="CoverImageException"><see cref="CoverErrorCodes.FileTooLarge"/>, worded by <paramref name="refusals"/>.</exception>
    public static async Task<byte[]> ReadUploadAsync(Stream stream, long maxBytes, ImageRefusalMessages refusals, CancellationToken cancellationToken)
    {
        using var buffer = new MemoryStream();
        var chunk = new byte[81920];
        int read;
        while ((read = await stream.ReadAsync(chunk, cancellationToken)) > 0)
        {
            if (buffer.Length + read > maxBytes)
            {
                throw new CoverImageException(CoverErrorCodes.FileTooLarge, refusals.FileTooLarge);
            }
            buffer.Write(chunk, 0, read);
        }
        return buffer.ToArray();
    }

    /// <summary>
    /// Halves the decoded image while the result needs at most half of its detail: repeated 2:1 box reductions keep
    /// fine detail (text) from aliasing, and the last resample is always by less than 2x. <paramref name="raw"/> is the
    /// source's stored size, <paramref name="neededScale"/> the scale from it to the result.
    /// </summary>
    public static SKImage HalveWhileLarge(SKImage decoded, SKSizeI raw, double neededScale)
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

    /// <summary>High-quality downscale: halve while more than 2x too large, then one bicubic step.</summary>
    public static SKImage Resize(SKImage source, int width, int height)
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

    /// <summary>
    /// A <paramref name="width"/> x <paramref name="height"/> opaque image drawn by <paramref name="draw"/> onto white,
    /// so transparency is flattened onto white. Its colour space is left unset on purpose: the pixels are already sRGB,
    /// and encoders then add no ICC profile.
    /// </summary>
    public static SKImage OnWhite(int width, int height, Action<SKCanvas> draw)
    {
        using var surface = SKSurface.Create(new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Opaque))
            ?? throw new InvalidOperationException($"Could not allocate a {width}x{height} surface.");
        var canvas = surface.Canvas;
        canvas.Clear(SKColors.White);
        draw(canvas);
        canvas.Flush();
        return surface.Snapshot();
    }

    /// <summary>
    /// Draws the part <paramref name="source"/> (in upright coordinates) of a stored image into
    /// <paramref name="destination"/>; <paramref name="orient"/> is its <see cref="ImageOrientation.Matrix"/>.
    /// </summary>
    public static void DrawUpright(SKCanvas canvas, SKImage image, SKMatrix orient, SKRect source, SKRect destination,
        SKSamplingOptions sampling, SKPaint? paint = null)
    {
        var scaleX = destination.Width / source.Width;
        var scaleY = destination.Height / source.Height;
        var place = SKMatrix.CreateScaleTranslation(scaleX, scaleY, destination.Left - source.Left * scaleX, destination.Top - source.Top * scaleY);

        canvas.Save();
        canvas.ClipRect(destination);
        canvas.SetMatrix(SKMatrix.Concat(place, orient));
        canvas.DrawImage(image, 0, 0, sampling, paint);
        canvas.Restore();
    }

    /// <summary>Lossy WebP at <see cref="WebpQuality"/>; an opaque image gives the simple format, with no room for metadata.</summary>
    public static byte[] EncodeWebp(SKImage image)
    {
        using var pixmap = image.PeekPixels() ?? throw new InvalidOperationException("Image pixels are not readable.");
        using var encoded = pixmap.Encode(new SKWebpEncoderOptions(SKWebpEncoderCompression.Lossy, WebpQuality))
            ?? throw new InvalidOperationException("WebP encoding failed.");
        return encoded.ToArray();
    }

    public static byte[] EncodeJpeg(SKImage image)
    {
        using var pixmap = image.PeekPixels() ?? throw new InvalidOperationException("Image pixels are not readable.");
        using var encoded = pixmap.Encode(new SKJpegEncoderOptions(JpegQuality, SKJpegEncoderDownsample.Downsample420, SKJpegEncoderAlphaOption.Ignore))
            ?? throw new InvalidOperationException("JPEG encoding failed.");
        return encoded.ToArray();
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
