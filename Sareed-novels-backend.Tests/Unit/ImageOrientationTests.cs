using Infrastructure.Services.Images;
using SkiaSharp;

namespace Sareed_novels_backend.Tests.Unit;

/// <summary>EXIF orientation, which covers and chapter pictures apply alike.</summary>
public class ImageOrientationTests
{
    [Theory]
    [InlineData(SKEncodedOrigin.TopLeft, 0, 0)]
    [InlineData(SKEncodedOrigin.TopRight, 300, 0)]
    [InlineData(SKEncodedOrigin.BottomRight, 300, 200)]
    [InlineData(SKEncodedOrigin.BottomLeft, 0, 200)]
    [InlineData(SKEncodedOrigin.LeftTop, 0, 0)]
    [InlineData(SKEncodedOrigin.RightTop, 200, 0)]      // EXIF 6, the usual phone portrait: stored top-left ends up top-right
    [InlineData(SKEncodedOrigin.RightBottom, 200, 300)]
    [InlineData(SKEncodedOrigin.LeftBottom, 0, 300)]
    public void Orientation_moves_the_stored_top_left_corner_where_viewers_show_it(SKEncodedOrigin origin, float x, float y)
    {
        // A stored 300x200 image.
        var matrix = ImageOrientation.Matrix(origin, 300, 200);

        Assert.Equal(new SKPoint(x, y), matrix.MapPoint(0, 0));

        // The whole image lands exactly on the upright canvas.
        var upright = ImageOrientation.UprightSize(300, 200, origin);
        var bounds = matrix.MapRect(new SKRect(0, 0, 300, 200));
        Assert.Equal(new SKRect(0, 0, upright.Width, upright.Height), bounds);
    }

    [Fact]
    public void Orientations_five_to_eight_swap_width_and_height()
    {
        Assert.Equal(new SKSizeI(300, 200), ImageOrientation.UprightSize(300, 200, SKEncodedOrigin.TopLeft));
        Assert.Equal(new SKSizeI(300, 200), ImageOrientation.UprightSize(300, 200, SKEncodedOrigin.BottomRight));
        Assert.Equal(new SKSizeI(200, 300), ImageOrientation.UprightSize(300, 200, SKEncodedOrigin.RightTop));
        Assert.Equal(new SKSizeI(200, 300), ImageOrientation.UprightSize(300, 200, SKEncodedOrigin.LeftBottom));
    }
}
