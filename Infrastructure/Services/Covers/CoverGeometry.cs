using Application.Covers;
using SkiaSharp;

namespace Infrastructure.Services.Covers;

/// <summary>How a source that isn't exactly 2:3 becomes a 2:3 cover.</summary>
public enum CoverLayout
{
    /// <summary>Centre-crop to 2:3 (the source was close to 2:3, so little is lost).</summary>
    Crop,

    /// <summary>
    /// The whole source, centred on a blurred, darkened copy of itself (a square or landscape image, or a very tall
    /// screenshot, where a crop would cut away a large part of the artwork and often its title).
    /// </summary>
    Fit,
}

/// <summary>What to cut from the (upright) source and how large the result is.</summary>
/// <param name="Layout">Crop or fit.</param>
/// <param name="Source">For <see cref="CoverLayout.Crop"/>, the 2:3 region of the upright source; for Fit, all of it.</param>
/// <param name="Width">Width of the largest cover file (even, so the 2:3 height is exact).</param>
/// <param name="Height">Its height.</param>
public readonly record struct CoverPlan(CoverLayout Layout, SKRectI Source, int Width, int Height)
{
    /// <summary>Scale from source pixels to cover pixels for the artwork itself.</summary>
    public double ContentScale => Layout == CoverLayout.Crop
        ? (double)Width / Source.Width
        : Math.Min((double)Width / Source.Width, (double)Height / Source.Height);
}

/// <summary>How much of an image a centre crop may remove (as a share of its area) before it is fitted whole instead.</summary>
/// <param name="Sides">For images wider than 2:3, whose left and right edges are cut.</param>
/// <param name="TopAndBottom">For images taller than 2:3, whose top and bottom edges are cut.</param>
public readonly record struct CropAllowance(double Sides, double TopAndBottom)
{
    /// <summary>
    /// Near-portrait art (up to about 8:9) is cropped, losing only side margins, which rarely hold text; that is how
    /// Wattpad-style thumbnails treat it, and a fitted near-portrait image leaves thin smudgy bars. Cutting the top and
    /// bottom reaches the title sooner, so tall images (phone screenshots) are cropped less and fitted sooner.
    /// </summary>
    public static readonly CropAllowance Default = new(0.25, 0.15);

    /// <summary>
    /// After solid bars were trimmed off: the artwork's top and bottom edges are then often its title, so only a sliver
    /// may go there; the sides are treated as usual.
    /// </summary>
    public static readonly CropAllowance AfterTrim = new(0.25, 0.02);
}

/// <summary>The arithmetic of turning an image into a cover. No pixels here, so it is cheap to test exhaustively.</summary>
public static class CoverGeometry
{
    /// <summary>Target width/height.</summary>
    public const double Ratio = (double)NovelCovers.RatioWidth / NovelCovers.RatioHeight;

    /// <summary>Sources this close to 2:3 are used whole (the rounding of a 2:3 export is not worth a crop).</summary>
    private const double RatioTolerance = 0.005;

    /// <summary>
    /// Plans the cover for an upright source of <paramref name="width"/> x <paramref name="height"/> pixels. The result is
    /// never upscaled: a source narrower than <see cref="NovelCovers.MaxWidth"/> gives a cover as wide as its 2:3 part.
    /// </summary>
    public static CoverPlan Plan(int width, int height, int maxWidth = NovelCovers.MaxWidth, CropAllowance? allowance = null)
    {
        if (width <= 0 || height <= 0) throw new ArgumentOutOfRangeException(nameof(width), "Image has no pixels.");
        var allowed = allowance ?? CropAllowance.Default;

        var ratio = (double)width / height;
        SKRectI crop;
        if (Math.Abs(ratio / Ratio - 1) <= RatioTolerance)
        {
            crop = new SKRectI(0, 0, width, height);
        }
        else if (ratio > Ratio)
        {
            // Wider than 2:3: keep the full height, cut the sides.
            var cropWidth = Math.Max(1, (int)Math.Round(height * Ratio));
            var left = (width - cropWidth) / 2;
            crop = new SKRectI(left, 0, left + cropWidth, height);
        }
        else
        {
            // Taller than 2:3: keep the full width, cut top and bottom.
            var cropHeight = Math.Max(1, (int)Math.Round(width / Ratio));
            var top = (height - cropHeight) / 2;
            crop = new SKRectI(0, top, width, top + cropHeight);
        }

        var loss = 1 - (double)crop.Width * crop.Height / ((double)width * height);
        if (loss <= (ratio > Ratio ? allowed.Sides : allowed.TopAndBottom))
        {
            var coverWidth = EvenWidth(Math.Min(maxWidth, crop.Width));
            return new CoverPlan(CoverLayout.Crop, crop, coverWidth, NovelCovers.HeightFor(coverWidth));
        }

        // Fit: the artwork's limiting side fills the cover, so the cover is as wide as that side allows without upscaling.
        var usefulWidth = ratio > Ratio ? width : (int)Math.Floor(height * Ratio);
        var fitWidth = EvenWidth(Math.Min(maxWidth, usefulWidth));
        return new CoverPlan(CoverLayout.Fit, new SKRectI(0, 0, width, height), fitWidth, NovelCovers.HeightFor(fitWidth));
    }

    /// <summary>Size of the source once its EXIF orientation is applied (orientations 5-8 swap width and height).</summary>
    public static SKSizeI UprightSize(int width, int height, SKEncodedOrigin origin) =>
        SwapsAxes(origin) ? new SKSizeI(height, width) : new SKSizeI(width, height);

    public static bool SwapsAxes(SKEncodedOrigin origin) =>
        origin is SKEncodedOrigin.LeftTop or SKEncodedOrigin.RightTop or SKEncodedOrigin.RightBottom or SKEncodedOrigin.LeftBottom;

    /// <summary>
    /// Maps pixels of a stored image of <paramref name="width"/> x <paramref name="height"/> to where they are seen when
    /// the EXIF orientation is honoured (the way browsers and phones show the photo).
    /// </summary>
    public static SKMatrix OrientationMatrix(SKEncodedOrigin origin, int width, int height) => origin switch
    {
        // x' = scaleX*x + skewX*y + transX, y' = skewY*x + scaleY*y + transY
        SKEncodedOrigin.TopRight => new SKMatrix(-1, 0, width, 0, 1, 0, 0, 0, 1),        // mirror horizontally
        SKEncodedOrigin.BottomRight => new SKMatrix(-1, 0, width, 0, -1, height, 0, 0, 1), // rotate 180
        SKEncodedOrigin.BottomLeft => new SKMatrix(1, 0, 0, 0, -1, height, 0, 0, 1),      // mirror vertically
        SKEncodedOrigin.LeftTop => new SKMatrix(0, 1, 0, 1, 0, 0, 0, 0, 1),               // transpose
        SKEncodedOrigin.RightTop => new SKMatrix(0, -1, height, 1, 0, 0, 0, 0, 1),        // rotate 90 clockwise
        SKEncodedOrigin.RightBottom => new SKMatrix(0, -1, height, -1, 0, width, 0, 0, 1), // transverse
        SKEncodedOrigin.LeftBottom => new SKMatrix(0, 1, 0, -1, 0, width, 0, 0, 1),       // rotate 90 counter-clockwise
        _ => SKMatrix.Identity,
    };

    private static int EvenWidth(int width) => Math.Max(2, width - width % 2);
}
