using System.Text;
using Application.Covers;
using Infrastructure.Services.Covers;
using Sareed_novels_backend.Tests.Integration;
using SkiaSharp;

namespace Sareed_novels_backend.Tests.Unit;

public class CoverImageProcessorTests
{
    private readonly CoverImageProcessor processor = new();

    private static readonly SKColor Red = new(220, 30, 30);
    private static readonly SKColor Blue = new(30, 60, 220);
    private static readonly SKColor Green = new(30, 180, 60);

    [Fact]
    public void The_native_library_loads_on_this_machine()
    {
        Assert.True(CoverImageProcessor.IsAvailable, CoverImageProcessor.AvailabilityError?.ToString());
    }

    [Fact]
    public void A_two_by_three_upload_becomes_three_webp_sizes_and_a_small_jpeg()
    {
        var cover = processor.Process(TestImages.Halves(1200, 1800, Red, Blue, SKEncodedImageFormat.Jpeg, vertical: true), enforceMinimumSize: true);

        Assert.Equal(CoverLayout.Crop, cover.Layout);
        Assert.Equal((960, 1440), (cover.Width, cover.Height));
        Assert.Equal(["960.webp", "640.webp", "320.webp", "cover.jpg", "og.jpg"], cover.Files.Select(f => f.FileName));
        Assert.Equal("960.webp", cover.Full.FileName);

        foreach (var file in cover.Files.Where(f => f.FileName != NovelCovers.ShareImageFileName))
        {
            using var decoded = TestImages.Decode(file.Bytes);
            Assert.Equal((file.Width, file.Height), (decoded.Width, decoded.Height));
            Assert.Equal(file.Width * 3 / 2, file.Height);
            // Top half red, bottom half blue: nothing was cropped or flipped.
            Assert.True(TestImages.Near(decoded.GetPixel(file.Width / 2, file.Height / 4), Red));
            Assert.True(TestImages.Near(decoded.GetPixel(file.Width / 2, file.Height * 3 / 4), Blue));
        }

        var jpeg = cover.Files.Single(f => f.FileName == NovelCovers.JpegFileName);
        Assert.Equal("image/jpeg", jpeg.ContentType);
        Assert.Equal(480, jpeg.Width);
    }

    [Fact]
    public void The_share_image_is_1200x630_with_the_cover_centred_on_a_blurred_darker_copy()
    {
        var cover = processor.Process(TestImages.Halves(900, 1350, Red, Blue, SKEncodedImageFormat.Png, vertical: true), enforceMinimumSize: true);
        var share = cover.Files.Single(f => f.FileName == NovelCovers.ShareImageFileName);

        Assert.Equal("image/jpeg", share.ContentType);
        Assert.True(share.Bytes.Length < 300_000, $"{share.Bytes.Length} bytes");
        using var decoded = TestImages.Decode(share.Bytes);
        Assert.Equal((1200, 630), (decoded.Width, decoded.Height));

        // The cover: 560 px tall, ~373 px wide, centred; red top half, blue bottom half.
        Assert.True(TestImages.Near(decoded.GetPixel(600, 150), Red), $"cover top is {decoded.GetPixel(600, 150)}");
        Assert.True(TestImages.Near(decoded.GetPixel(600, 480), Blue), $"cover bottom is {decoded.GetPixel(600, 480)}");

        // Beside it: the backdrop, darker than the cover and not a flat fill (blurred cover colours).
        var right = decoded.GetPixel(1100, 150);
        Assert.True(right.Red < Red.Red && right.Red > right.Blue, $"backdrop is {right}");

        // Bottom-left: the wordmark, whiter than the backdrop right above it.
        var markArea = Enumerable.Range(40, 70).Max(x => (int)decoded.GetPixel(x, 575).Green);
        Assert.True(markArea > 150, $"wordmark brightness {markArea}");
    }

    [Fact]
    public void The_default_share_image_is_a_branded_placeholder()
    {
        using var decoded = TestImages.Decode(CoverImageProcessor.DefaultShareImage());

        Assert.Equal((1200, 630), (decoded.Width, decoded.Height));
        // The wordmark in the middle of the placeholder cover is white.
        var centre = Enumerable.Range(560, 80).Max(x => (int)decoded.GetPixel(x, 315).Red);
        Assert.True(centre > 200, $"centre brightness {centre}");
    }

    [Fact]
    public void WebP_files_are_plain_lossy_webp_without_metadata_or_alpha()
    {
        var cover = processor.Process(TestImages.Halves(900, 1350, Red, Blue, SKEncodedImageFormat.Png), enforceMinimumSize: true);

        foreach (var file in cover.Files.Where(f => f.ContentType == "image/webp"))
        {
            Assert.Equal("RIFF", Encoding.ASCII.GetString(file.Bytes, 0, 4));
            Assert.Equal("WEBP", Encoding.ASCII.GetString(file.Bytes, 8, 4));
            // "VP8 " is the simple lossy format: no VP8X header, so no EXIF, XMP, ICC or alpha chunks.
            Assert.Equal("VP8 ", Encoding.ASCII.GetString(file.Bytes, 12, 4));
        }
    }

    [Fact]
    public void A_phone_photo_is_turned_upright_and_its_exif_and_gps_are_dropped()
    {
        // Stored landscape 1200x800, left half red, right half blue, EXIF orientation 6 (rotate 90 clockwise to view):
        // viewers show it 800x1200 with red on top and blue at the bottom.
        var photo = TestImages.WithExifOrientation(TestImages.Halves(1200, 800, Red, Blue, SKEncodedImageFormat.Jpeg), orientation: 6);
        Assert.Contains("Exif", Encoding.ASCII.GetString(photo));

        var cover = processor.Process(photo, enforceMinimumSize: true);

        Assert.Equal((800, 1200), (cover.SourceWidth, cover.SourceHeight));
        Assert.Equal(CoverLayout.Crop, cover.Layout);
        Assert.Equal((800, 1200), (cover.Width, cover.Height));
        using var decoded = TestImages.Decode(cover.Full.Bytes);
        Assert.True(TestImages.Near(decoded.GetPixel(400, 200), Red), $"top is {decoded.GetPixel(400, 200)}");
        Assert.True(TestImages.Near(decoded.GetPixel(400, 1000), Blue), $"bottom is {decoded.GetPixel(400, 1000)}");

        foreach (var file in cover.Files)
        {
            var text = Encoding.ASCII.GetString(file.Bytes);
            Assert.DoesNotContain("Exif", text);
            Assert.DoesNotContain("Phone", text);
        }
    }

    [Fact]
    public void Rotate_90_counter_clockwise_is_applied()
    {
        // EXIF 8: the stored left (red) half ends up at the bottom.
        var photo = TestImages.WithExifOrientation(TestImages.Halves(1200, 800, Red, Blue, SKEncodedImageFormat.Jpeg), orientation: 8);

        var cover = processor.Process(photo, enforceMinimumSize: true);
        using var decoded = TestImages.Decode(cover.Full.Bytes);

        Assert.Equal((800, 1200), (cover.SourceWidth, cover.SourceHeight));
        Assert.True(TestImages.Near(decoded.GetPixel(decoded.Width / 2, decoded.Height / 4), Blue));
        Assert.True(TestImages.Near(decoded.GetPixel(decoded.Width / 2, decoded.Height * 3 / 4), Red));
    }

    [Fact]
    public void Rotate_180_is_applied()
    {
        // EXIF 3: stays landscape (so it is fitted), red and blue swap sides.
        var photo = TestImages.WithExifOrientation(TestImages.Halves(1200, 800, Red, Blue, SKEncodedImageFormat.Jpeg), orientation: 3);

        var cover = processor.Process(photo, enforceMinimumSize: true);
        using var decoded = TestImages.Decode(cover.Full.Bytes);

        Assert.Equal((1200, 800), (cover.SourceWidth, cover.SourceHeight));
        Assert.Equal(CoverLayout.Fit, cover.Layout);
        Assert.True(TestImages.Near(decoded.GetPixel(decoded.Width / 4, decoded.Height / 2), Blue));
        Assert.True(TestImages.Near(decoded.GetPixel(decoded.Width * 3 / 4, decoded.Height / 2), Red));
    }

    [Fact]
    public void A_landscape_image_is_fitted_whole_on_a_blurred_backdrop_of_itself()
    {
        var cover = processor.Process(TestImages.Halves(1600, 900, Red, Green, SKEncodedImageFormat.Jpeg), enforceMinimumSize: true);

        Assert.Equal(CoverLayout.Fit, cover.Layout);
        Assert.Equal((960, 1440), (cover.Width, cover.Height));
        using var decoded = TestImages.Decode(cover.Full.Bytes);

        // The artwork: 960x540, centred vertically, both halves intact.
        Assert.True(TestImages.Near(decoded.GetPixel(100, 720), Red));
        Assert.True(TestImages.Near(decoded.GetPixel(860, 720), Green));

        // Above and below it: the blurred, darkened backdrop, never white or empty.
        var above = decoded.GetPixel(100, 100);
        Assert.False(TestImages.Near(above, SKColors.White, 60), $"backdrop is {above}");
        Assert.True(above.Red > above.Green, "the backdrop on the red side is reddish");
        Assert.True(above.Red < Red.Red, "the backdrop is darkened");
    }

    [Fact]
    public void Wattpad_sized_covers_are_cropped_not_fitted()
    {
        var cover = processor.Process(TestImages.Halves(512, 800, Red, Blue, SKEncodedImageFormat.Jpeg, vertical: true), enforceMinimumSize: true);

        Assert.Equal(CoverLayout.Crop, cover.Layout);
        Assert.Equal((512, 768), (cover.Width, cover.Height));
        Assert.Equal(["512.webp", "320.webp", "cover.jpg", "og.jpg"], cover.Files.Select(f => f.FileName));
    }

    [Fact]
    public void Transparent_areas_are_flattened_onto_white()
    {
        var cover = processor.Process(TestImages.TransparentLeftHalf(600, 900, Green), enforceMinimumSize: true);
        using var decoded = TestImages.Decode(cover.Full.Bytes);

        Assert.Equal((600, 900), (cover.Width, cover.Height));
        Assert.True(TestImages.Near(decoded.GetPixel(150, 450), SKColors.White, 8), $"left half is {decoded.GetPixel(150, 450)}");
        Assert.True(TestImages.Near(decoded.GetPixel(450, 450), Green));
        Assert.Equal(255, decoded.GetPixel(150, 450).Alpha);
    }

    [Fact]
    public void White_pillarbox_bars_are_removed_before_cropping()
    {
        // A square image made from a 2:3 cover with white bars pasted either side (seen twice in production).
        var boxed = TestImages.Boxed(1200, 1200, new SKRect(200, 0, 1000, 1200), SKColors.White, Red, Blue);

        var cover = processor.Process(boxed, enforceMinimumSize: true);
        using var decoded = TestImages.Decode(cover.Full.Bytes);

        Assert.Equal(CoverLayout.Crop, cover.Layout);
        Assert.Equal((800, 1200), (cover.Width, cover.Height));
        Assert.True(TestImages.Near(decoded.GetPixel(10, 10), Red), $"top-left is {decoded.GetPixel(10, 10)}");
        Assert.True(TestImages.Near(decoded.GetPixel(790, 1190), Blue), $"bottom-right is {decoded.GetPixel(790, 1190)}");
    }

    [Fact]
    public void Black_letterbox_bars_of_a_phone_screenshot_are_removed()
    {
        // 1080x2340 screenshot with a 1080x1620 (2:3) cover in the middle.
        var screenshot = TestImages.Boxed(1080, 2340, new SKRect(0, 360, 1080, 1980), SKColors.Black, Red, Blue, SKEncodedImageFormat.Png);

        var cover = processor.Process(screenshot, enforceMinimumSize: true);
        using var decoded = TestImages.Decode(cover.Full.Bytes);

        Assert.Equal(CoverLayout.Crop, cover.Layout);
        Assert.Equal((960, 1440), (cover.Width, cover.Height));
        Assert.True(TestImages.Near(decoded.GetPixel(480, 10), Red), $"top is {decoded.GetPixel(480, 10)}");
        Assert.True(TestImages.Near(decoded.GetPixel(480, 1430), Blue), $"bottom is {decoded.GetPixel(480, 1430)}");
    }

    [Fact]
    public void A_plain_area_on_one_side_only_is_part_of_the_artwork_and_kept()
    {
        // A 2:3 cover whose top third is plain white sky: not a bar pair, so nothing is trimmed.
        var sky = TestImages.Boxed(900, 1350, new SKRect(0, 450, 900, 1350), SKColors.White, Red, Blue);

        var cover = processor.Process(sky, enforceMinimumSize: true);
        using var decoded = TestImages.Decode(cover.Full.Bytes);

        Assert.Equal((900, 1350), (cover.Width, cover.Height));
        Assert.True(TestImages.Near(decoded.GetPixel(450, 100), SKColors.White, 8));
        Assert.True(TestImages.Near(decoded.GetPixel(450, 600), Red));
    }

    [Fact]
    public void Bars_that_do_not_match_on_both_sides_are_kept()
    {
        // White on the left, black on the right: a design, not a pillarbox.
        using var surface = SKSurface.Create(new SKImageInfo(900, 1350));
        surface.Canvas.Clear(Red);
        using (var white = new SKPaint { Color = SKColors.White }) surface.Canvas.DrawRect(0, 0, 150, 1350, white);
        using (var black = new SKPaint { Color = SKColors.Black }) surface.Canvas.DrawRect(750, 0, 150, 1350, black);
        using var image = surface.Snapshot();
        using var png = image.Encode(SKEncodedImageFormat.Png, 100);

        var cover = processor.Process(png.ToArray(), enforceMinimumSize: true);
        using var decoded = TestImages.Decode(cover.Full.Bytes);

        Assert.Equal((900, 1350), (cover.Width, cover.Height));
        Assert.True(TestImages.Near(decoded.GetPixel(20, 600), SKColors.White, 8));
        Assert.True(TestImages.Near(decoded.GetPixel(880, 600), SKColors.Black, 8));
    }

    [Fact]
    public void WebP_uploads_are_accepted()
    {
        var cover = processor.Process(TestImages.Halves(700, 1050, Red, Blue, SKEncodedImageFormat.Webp), enforceMinimumSize: true);
        Assert.Equal(700, cover.Width);
        Assert.Equal(SKEncodedImageFormat.Webp, cover.SourceFormat);
    }

    [Fact]
    public void A_large_photo_is_reduced_to_the_standard_width()
    {
        var cover = processor.Process(TestImages.Halves(4000, 6000, Red, Blue, SKEncodedImageFormat.Jpeg, vertical: true, quality: 80), enforceMinimumSize: true);

        Assert.Equal((960, 1440), (cover.Width, cover.Height));
        Assert.True(cover.Full.Bytes.Length < 200_000);
        using var decoded = TestImages.Decode(cover.Full.Bytes);
        Assert.True(TestImages.Near(decoded.GetPixel(480, 300), Red));
        Assert.True(TestImages.Near(decoded.GetPixel(480, 1140), Blue));
    }

    [Fact]
    public void Uploads_below_the_minimum_are_refused_but_existing_small_covers_are_kept()
    {
        var tiny = TestImages.Halves(200, 300, Red, Blue, SKEncodedImageFormat.Png);

        var refused = Assert.Throws<CoverImageException>(() => processor.Process(tiny, enforceMinimumSize: true));
        Assert.Equal(CoverErrorCodes.TooSmall, refused.Code);

        var kept = processor.Process(tiny, enforceMinimumSize: false);
        Assert.Equal((200, 300), (kept.Width, kept.Height));
        Assert.Equal(["200.webp", "cover.jpg", "og.jpg"], kept.Files.Select(f => f.FileName));
    }

    [Fact]
    public void Files_that_are_not_images_are_refused()
    {
        var text = Assert.Throws<CoverImageException>(() => processor.Process("this is not an image"u8.ToArray(), true));
        Assert.Equal(CoverErrorCodes.UnsupportedFormat, text.Code);

        var gif = Convert.FromBase64String("R0lGODlhAQABAIAAAAAAAP///ywAAAAAAQABAAACAUwAOw==");
        var gifError = Assert.Throws<CoverImageException>(() => processor.Process(gif, true));
        Assert.Equal(CoverErrorCodes.UnsupportedFormat, gifError.Code);
    }

    [Fact]
    public void A_truncated_image_is_refused_as_unreadable()
    {
        var jpeg = TestImages.Halves(900, 1350, Red, Blue, SKEncodedImageFormat.Jpeg);

        var error = Assert.Throws<CoverImageException>(() => processor.Process(jpeg[..(jpeg.Length / 2)], true));

        Assert.Equal(CoverErrorCodes.Unreadable, error.Code);
    }

    [Theory]
    [InlineData(20000, 20000)]  // 400 MP: refused from the header, nothing decoded
    [InlineData(5000, 5000)]    // 25 MP PNG: under the source limit, but PNG can't be decoded at a reduced scale
    public void Images_with_too_many_pixels_are_refused_before_decoding(int width, int height)
    {
        var error = Assert.Throws<CoverImageException>(() => processor.Process(TestImages.PngHeaderOnly(width, height), true));

        Assert.Equal(CoverErrorCodes.TooManyPixels, error.Code);
    }

    [Fact]
    public void Output_is_the_same_for_the_same_input()
    {
        var input = TestImages.Halves(900, 1400, Red, Blue, SKEncodedImageFormat.Png);

        var first = processor.Process(input, true);
        var second = processor.Process(input, true);

        Assert.Equal(first.Files.Select(f => f.Bytes), second.Files.Select(f => f.Bytes));
    }
}
