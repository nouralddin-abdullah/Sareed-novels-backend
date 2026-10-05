using Application.Chapters;
using Application.Covers;
using SkiaSharp;

namespace Infrastructure.Services.Images;

/// <summary>A chapter picture ready to store: one WebP file of <see cref="Width"/> x <see cref="Height"/>.</summary>
public sealed record ProcessedChapterImage(
    SKEncodedImageFormat SourceFormat,
    int SourceWidth,
    int SourceHeight,
    int Width,
    int Height,
    byte[] Bytes);

/// <summary>
/// Turns an uploaded picture into a chapter picture (<see cref="ChapterImages"/>) with the cover's pipeline
/// (<see cref="SourceImage"/>, <see cref="ImagePipeline"/>) and none of the cover's 2:3 rules: EXIF orientation applied,
/// converted to sRGB, the whole picture kept at its aspect ratio, its long side at most
/// <see cref="ChapterImages.MaxLongSide"/> px (never enlarged), transparency flattened onto white, written as lossy WebP
/// at the cover's quality (<see cref="ImagePipeline.WebpQuality"/>) with no metadata (no EXIF, GPS or ICC).
/// </summary>
public sealed class ChapterImageProcessor
{
    /// <exception cref="CoverImageException">The bytes aren't a usable JPEG, PNG or WebP image.</exception>
    public ProcessedChapterImage Process(byte[] bytes)
    {
        using var source = SourceImage.Open(bytes, ChapterImages.Refusals);
        var upright = source.Upright;
        var (width, height) = ChapterImages.SizeFor(upright.Width, upright.Height);
        var scale = Math.Max((double)width / upright.Width, (double)height / upright.Height);

        using var decoded = source.Decode(scale);
        var reduced = ImagePipeline.HalveWhileLarge(decoded, source.Size, scale);
        try
        {
            using var picture = Render(reduced, source.Origin, width, height);
            return new ProcessedChapterImage(source.Format, upright.Width, upright.Height, width, height, ImagePipeline.EncodeWebp(picture));
        }
        finally
        {
            if (!ReferenceEquals(reduced, decoded)) reduced.Dispose();
        }
    }

    /// <summary>Draws the whole picture upright at <paramref name="width"/> x <paramref name="height"/> onto white.</summary>
    private static SKImage Render(SKImage image, SKEncodedOrigin origin, int width, int height)
    {
        var orient = ImageOrientation.Matrix(origin, image.Width, image.Height);
        var upright = ImageOrientation.UprightSize(image.Width, image.Height, origin);
        // A picture already at its stored size (a small one, or a JPEG decoded at exactly that scale) is copied pixel for
        // pixel; anything larger gets the cover's bicubic step.
        var sampling = upright.Width == width && upright.Height == height ? ImagePipeline.Exact : ImagePipeline.Final;

        return ImagePipeline.OnWhite(width, height, canvas =>
            ImagePipeline.DrawUpright(canvas, image, orient, new SKRect(0, 0, upright.Width, upright.Height),
                new SKRect(0, 0, width, height), sampling));
    }
}
