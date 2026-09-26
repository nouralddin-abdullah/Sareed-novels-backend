using Application.Covers;
using Infrastructure.Services.Covers;
using SkiaSharp;

namespace Sareed_novels_backend.Tests.Unit;

public class CoverGeometryTests
{
    [Theory]
    // Exactly 2:3, or within rounding of it: used whole, capped at 960 wide.
    [InlineData(1200, 1800, 0, 0, 1200, 1800, 960)]
    [InlineData(640, 960, 0, 0, 640, 960, 640)]
    [InlineData(1024, 1537, 0, 0, 1024, 1537, 960)]
    // Wattpad's 512x800 (0.64): 16 px off the top and bottom.
    [InlineData(512, 800, 0, 16, 512, 784, 512)]
    // A-series 1054x1492 (0.706): 29-30 px off each side.
    [InlineData(1054, 1492, 29, 0, 1024, 1492, 960)]
    // 3:4 phone photo (0.75): the crop loses 11%, still a crop.
    [InlineData(3024, 4032, 168, 0, 2856, 4032, 960)]
    // Near-portrait art from production (0.81-0.83): the sides go (17-20%), which beats thin blurred bars top and bottom.
    [InlineData(800, 973, 75, 0, 724, 973, 648)]
    [InlineData(800, 958, 80, 0, 719, 958, 638)]
    [InlineData(735, 910, 64, 0, 671, 910, 606)]
    public void Near_two_by_three_sources_are_centre_cropped(int width, int height, int left, int top, int right, int bottom, int coverWidth)
    {
        var plan = CoverGeometry.Plan(width, height);

        Assert.Equal(CoverLayout.Crop, plan.Layout);
        Assert.Equal(new SKRectI(left, top, right, bottom), plan.Source);
        Assert.Equal(coverWidth, plan.Width);
        Assert.Equal(coverWidth * 3 / 2, plan.Height);
    }

    [Fact]
    public void Sides_may_lose_more_than_top_and_bottom_where_titles_usually_are()
    {
        // The same 16.7% loss: from the sides of a 4:5 image it is a crop, from the top and bottom of a 5:9 one a fit.
        Assert.Equal(CoverLayout.Crop, CoverGeometry.Plan(1200, 1500).Layout);
        Assert.Equal(CoverLayout.Fit, CoverGeometry.Plan(1000, 1800).Layout);

        // Up to a quarter from the sides (8:9 is just inside), no more.
        Assert.Equal(CoverLayout.Crop, CoverGeometry.Plan(1600, 1800).Layout);
        Assert.Equal(CoverLayout.Fit, CoverGeometry.Plan(1700, 1800).Layout);
    }

    [Fact]
    public void After_bars_are_trimmed_top_and_bottom_only_lose_a_sliver()
    {
        Assert.Equal(CoverLayout.Fit, CoverGeometry.Plan(1080, 1800, allowance: CropAllowance.AfterTrim).Layout);
        Assert.Equal(CoverLayout.Crop, CoverGeometry.Plan(1080, 1650, allowance: CropAllowance.AfterTrim).Layout);
        Assert.Equal(CoverLayout.Crop, CoverGeometry.Plan(800, 960, allowance: CropAllowance.AfterTrim).Layout);
    }

    [Fact]
    public void Wattpad_covers_keep_their_full_width_and_lose_two_percent_top_and_bottom()
    {
        var plan = CoverGeometry.Plan(512, 800);

        Assert.Equal(new SKRectI(0, 16, 512, 784), plan.Source);
        Assert.Equal((512, 768), (plan.Width, plan.Height));
    }

    [Theory]
    [InlineData(1254, 1254)]  // square
    [InlineData(1402, 1122)]  // landscape
    [InlineData(605, 435)]    // small landscape
    [InlineData(1080, 2340)]  // phone screenshot (0.46)
    [InlineData(720, 1600)]   // 0.45
    [InlineData(1080, 1920)]  // 9:16, a crop would lose 16%
    public void Far_from_two_by_three_sources_are_fitted_whole(int width, int height)
    {
        var plan = CoverGeometry.Plan(width, height);

        Assert.Equal(CoverLayout.Fit, plan.Layout);
        Assert.Equal(new SKRectI(0, 0, width, height), plan.Source);
        Assert.Equal(plan.Width * 3 / 2, plan.Height);
        Assert.True(plan.ContentScale <= 1, "Never upscaled");
        // The artwork's limiting side fills the cover.
        var fitted = (Width: width * plan.ContentScale, Height: height * plan.ContentScale);
        Assert.True(Math.Abs(fitted.Width - plan.Width) < 1 || Math.Abs(fitted.Height - plan.Height) < 1);
    }

    [Theory]
    [InlineData(605, 435, 604)]    // landscape: as wide as the source
    [InlineData(335, 597, 398)]    // tall and small: as wide as 2:3 of its height
    [InlineData(1080, 2340, 960)]
    public void Fitted_covers_are_never_wider_than_the_source_allows(int width, int height, int coverWidth)
    {
        Assert.Equal(coverWidth, CoverGeometry.Plan(width, height).Width);
    }

    [Theory]
    [InlineData(300, 450, 300)]
    [InlineData(301, 451, 300)]  // widths are even so the 2:3 height is exact
    [InlineData(2, 3, 2)]
    public void Small_sources_are_not_upscaled(int width, int height, int coverWidth)
    {
        var plan = CoverGeometry.Plan(width, height);
        Assert.Equal(coverWidth, plan.Width);
        Assert.Equal(coverWidth * 3 / 2, plan.Height);
    }

    [Fact]
    public void Plans_are_valid_for_every_shape()
    {
        for (var width = 1; width <= 3000; width += 37)
        for (var height = 1; height <= 3000; height += 41)
        {
            var plan = CoverGeometry.Plan(width, height);
            Assert.True(plan.Source.Left >= 0 && plan.Source.Top >= 0 && plan.Source.Right <= width && plan.Source.Bottom <= height);
            Assert.True(plan.Width is >= 2 and <= NovelCovers.MaxWidth && plan.Width % 2 == 0);
            Assert.Equal(plan.Width * 3 / 2, plan.Height);
            Assert.True(plan.ContentScale > 0);
        }
    }

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
        var matrix = CoverGeometry.OrientationMatrix(origin, 300, 200);

        Assert.Equal(new SKPoint(x, y), matrix.MapPoint(0, 0));

        // The whole image lands exactly on the upright canvas.
        var upright = CoverGeometry.UprightSize(300, 200, origin);
        var bounds = matrix.MapRect(new SKRect(0, 0, 300, 200));
        Assert.Equal(new SKRect(0, 0, upright.Width, upright.Height), bounds);
    }

    [Fact]
    public void Orientations_five_to_eight_swap_width_and_height()
    {
        Assert.Equal(new SKSizeI(300, 200), CoverGeometry.UprightSize(300, 200, SKEncodedOrigin.TopLeft));
        Assert.Equal(new SKSizeI(300, 200), CoverGeometry.UprightSize(300, 200, SKEncodedOrigin.BottomRight));
        Assert.Equal(new SKSizeI(200, 300), CoverGeometry.UprightSize(300, 200, SKEncodedOrigin.RightTop));
        Assert.Equal(new SKSizeI(200, 300), CoverGeometry.UprightSize(300, 200, SKEncodedOrigin.LeftBottom));
    }
}

public class NovelCoversTests
{
    private const string Base = "https://pub-test.r2.dev";

    [Fact]
    public void Standard_urls_are_recognised_and_legacy_ones_are_not()
    {
        var url = $"{Base}/novel-covers/33f570e8-cce3-4d69-8264-7e6dca75e5c6/0123456789abcdef0123456789abcdef/960.webp";

        Assert.True(NovelCovers.IsStandard(url));
        Assert.Equal(960, NovelCovers.FullWidthOf(url));
        Assert.Equal($"{Base}/novel-covers/33f570e8-cce3-4d69-8264-7e6dca75e5c6/0123456789abcdef0123456789abcdef/cover.jpg", NovelCovers.JpegUrlOf(url));

        Assert.False(NovelCovers.IsStandard($"{Base}/novel-images/الزائر الغريب "));
        Assert.False(NovelCovers.IsStandard($"{Base}/novel-images/%D8%A7.webp"));
        Assert.False(NovelCovers.IsStandard($"{Base}/novel-images/33f570e8-cce3-4d69-8264-7e6dca75e5c6/0123456789abcdef0123456789abcdef.png"));
        Assert.Null(NovelCovers.FullWidthOf("https://example.test/cover.png"));
        Assert.Null(NovelCovers.JpegUrlOf(null));
    }

    [Fact]
    public void Every_standard_url_contains_the_marker_used_in_sql_and_legacy_ones_do_not()
    {
        var url = $"{Base}/{NovelCovers.KeyPrefix(Guid.NewGuid(), Guid.NewGuid())}/960.webp";

        Assert.True(NovelCovers.IsStandard(url));
        Assert.Contains(NovelCovers.StandardUrlMarker, url);
        Assert.DoesNotContain(NovelCovers.StandardUrlMarker, $"{Base}/novel-images/novel-covers title");
    }

    [Theory]
    [InlineData(960, new[] { 320, 640, 960 })]
    [InlineData(800, new[] { 320, 640, 800 })]
    [InlineData(640, new[] { 320, 640 })]
    [InlineData(604, new[] { 320, 604 })]
    [InlineData(320, new[] { 320 })]
    [InlineData(300, new[] { 300 })]
    public void The_stored_widths_are_the_standard_ones_below_the_full_width_plus_the_full_width(int full, int[] widths)
    {
        Assert.Equal(widths, NovelCovers.WidthsFor(full));
    }
}
