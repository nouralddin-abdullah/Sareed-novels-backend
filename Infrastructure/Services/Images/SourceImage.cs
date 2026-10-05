using Application.Covers;
using SkiaSharp;

namespace Infrastructure.Services.Images;

/// <summary>
/// An uploaded picture opened for processing: the first steps covers (<see cref="Covers.CoverImageProcessor"/>) and
/// chapter pictures (<see cref="ChapterImageProcessor"/>) share. Opening checks that the bytes are a JPEG,
/// PNG or WebP image (by content, whatever the file name or declared type says) of at most
/// <see cref="MaxSourcePixels"/> and reads its EXIF orientation; <see cref="Decode"/> decodes it to sRGB at the scale
/// the result needs.
/// </summary>
/// <remarks>
/// Memory is bounded for a small shared host: the pixel count is capped before anything is decoded, JPEG and WebP
/// sources are decoded at a reduced scale when the result needs fewer pixels, and callers run one image at a time
/// (<see cref="ImagePipeline.OneAtATimeAsync{T}"/>). The largest decode allowed is <see cref="MaxDecodedPixels"/>
/// (64 MB of RGBA).
/// </remarks>
public sealed class SourceImage : IDisposable
{
    /// <summary>Sources with more pixels are refused before anything is decoded (e.g. a PNG "decompression bomb").</summary>
    public const long MaxSourcePixels = 50_000_000;

    /// <summary>Most pixels ever decoded at once. PNG can't be decoded at a reduced scale, so a larger PNG is refused.</summary>
    public const long MaxDecodedPixels = 16_777_216;

    private readonly SKData data;
    private readonly SKCodec codec;

    private SourceImage(SKData data, SKCodec codec)
    {
        this.data = data;
        this.codec = codec;
        Size = codec.Info.Size;
        Origin = codec.EncodedOrigin;
        Upright = ImageOrientation.UprightSize(Size.Width, Size.Height, Origin);
    }

    public SKEncodedImageFormat Format => codec.EncodedFormat;

    /// <summary>The size as stored.</summary>
    public SKSizeI Size { get; }

    /// <summary>The EXIF orientation (<see cref="SKEncodedOrigin.TopLeft"/> when there is none).</summary>
    public SKEncodedOrigin Origin { get; }

    /// <summary>The size as viewers show it, <see cref="Origin"/> applied.</summary>
    public SKSizeI Upright { get; }

    /// <summary>
    /// Opens an upload. <paramref name="refusals"/> words the refusals that name what is uploaded (a cover, a chapter
    /// picture).
    /// </summary>
    /// <exception cref="CoverImageException">The bytes aren't a JPEG, PNG or WebP image, or it has too many pixels.</exception>
    public static SourceImage Open(byte[] bytes, ImageRefusalMessages refusals)
    {
        var data = SKData.CreateCopy(bytes);
        var codec = SKCodec.Create(data);
        if (codec is null)
        {
            data.Dispose();
            throw new CoverImageException(CoverErrorCodes.UnsupportedFormat, refusals.NotAnImage);
        }

        var image = new SourceImage(data, codec);
        try
        {
            image.Check(refusals);
            return image;
        }
        catch
        {
            image.Dispose();
            throw;
        }
    }

    private void Check(ImageRefusalMessages refusals)
    {
        if (Format is not (SKEncodedImageFormat.Jpeg or SKEncodedImageFormat.Png or SKEncodedImageFormat.Webp))
        {
            throw new CoverImageException(CoverErrorCodes.UnsupportedFormat, refusals.NotAnImageOf(Format.ToString()));
        }
        if (Size.Width <= 0 || Size.Height <= 0)
        {
            throw new CoverImageException(CoverErrorCodes.Unreadable, "الصورة فارغة.");
        }
        if ((long)Size.Width * Size.Height > MaxSourcePixels)
        {
            throw new CoverImageException(CoverErrorCodes.TooManyPixels, $"أبعاد الصورة كبيرة جداً ({Size.Width}×{Size.Height})؛ استخدم صورة أقل من {MaxSourcePixels / 1_000_000} ميغابكسل.");
        }
    }

    /// <summary>
    /// Decodes to sRGB (as stored, not yet turned upright), at the smallest JPEG/WebP scale (n/8) that keeps
    /// <paramref name="neededScale"/> of the detail; PNG always at full size.
    /// </summary>
    /// <exception cref="CoverImageException">Too many pixels to decode, or the data is truncated or corrupt.</exception>
    public SKImage Decode(double neededScale)
    {
        var raw = Size;
        var size = raw;
        if (neededScale < 1 && Format is SKEncodedImageFormat.Jpeg or SKEncodedImageFormat.Webp)
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

    public void Dispose()
    {
        codec.Dispose();
        data.Dispose();
    }
}
