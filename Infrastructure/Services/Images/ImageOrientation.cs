using SkiaSharp;

namespace Infrastructure.Services.Images;

/// <summary>EXIF orientation: where the pixels of a stored image are seen once it is turned the way the camera meant.</summary>
public static class ImageOrientation
{
    /// <summary>Size of the source once its EXIF orientation is applied (orientations 5-8 swap width and height).</summary>
    public static SKSizeI UprightSize(int width, int height, SKEncodedOrigin origin) =>
        SwapsAxes(origin) ? new SKSizeI(height, width) : new SKSizeI(width, height);

    public static bool SwapsAxes(SKEncodedOrigin origin) =>
        origin is SKEncodedOrigin.LeftTop or SKEncodedOrigin.RightTop or SKEncodedOrigin.RightBottom or SKEncodedOrigin.LeftBottom;

    /// <summary>
    /// Maps pixels of a stored image of <paramref name="width"/> x <paramref name="height"/> to where they are seen when
    /// the EXIF orientation is honoured (the way browsers and phones show the photo).
    /// </summary>
    public static SKMatrix Matrix(SKEncodedOrigin origin, int width, int height) => origin switch
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
}
