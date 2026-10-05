using System.Text;
using Application.Chapters;
using Application.Chapters.Paragraphs;
using Application.Covers;
using Infrastructure.Services.Images;
using Infrastructure.Services.Storage;
using Sareed_novels_backend.Tests.Integration;
using SkiaSharp;

namespace Sareed_novels_backend.Tests.Unit;

/// <summary>Chapter pictures (#86): their size, where they are stored, and the processing (the cover's pipeline).</summary>
public class ChapterImageTests
{
    private static readonly SKColor Red = new(220, 30, 30);
    private static readonly SKColor Blue = new(30, 60, 220);
    private static readonly SKColor Green = new(30, 180, 60);

    private readonly ChapterImageProcessor processor = new();

    [Theory]
    [InlineData(800, 600, 800, 600)]       // small: as it is, never enlarged
    [InlineData(2000, 1500, 2000, 1500)]   // exactly the limit
    [InlineData(1, 1, 1, 1)]
    [InlineData(4000, 3000, 2000, 1500)]   // landscape
    [InlineData(3000, 2000, 2000, 1333)]
    [InlineData(2000, 3000, 1333, 2000)]   // portrait
    [InlineData(1080, 2400, 900, 2000)]    // a phone screenshot
    [InlineData(2400, 2400, 2000, 2000)]
    [InlineData(100, 20000, 10, 2000)]     // a long strip keeps its ratio
    [InlineData(30000, 10, 2000, 1)]       // never 0 px
    public void The_long_side_is_at_most_2000_px_and_the_ratio_is_kept(int width, int height, int storedWidth, int storedHeight)
    {
        Assert.Equal((storedWidth, storedHeight), ChapterImages.SizeFor(width, height));
    }

    [Theory]
    [InlineData("https://files.test")]
    [InlineData("https://pub-test.r2.dev/")]
    public void A_stored_picture_is_under_its_novel_and_format_v1_keeps_its_address_unchanged(string publicBase)
    {
        var novelId = Guid.Parse("8bbea80e-6f4c-4ea5-9042-d0ee665e89a3");
        var imageId = Guid.Parse("244e2a84-0594-48e8-914f-cb2544fbd84a");

        var key = ChapterImages.Key(novelId, imageId);
        var url = StorageKeys.PublicUrl(publicBase, key);

        Assert.Equal("chapter-images/8bbea80e-6f4c-4ea5-9042-d0ee665e89a3/244e2a84059448e8914fcb2544fbd84a.webp", key);
        Assert.StartsWith(ChapterImages.NovelPrefix(novelId), key);
        Assert.Equal(url, ChapterFormat.ImageAddress(url));
    }

    [Fact]
    public void A_picture_within_the_limit_keeps_its_size_and_becomes_a_plain_webp()
    {
        var picture = processor.Process(TestImages.Halves(640, 480, Red, Blue, SKEncodedImageFormat.Jpeg));

        Assert.Equal((640, 480, 640, 480), (picture.SourceWidth, picture.SourceHeight, picture.Width, picture.Height));
        Assert.Equal(SKEncodedImageFormat.Jpeg, picture.SourceFormat);
        Assert.Equal("RIFF", Encoding.ASCII.GetString(picture.Bytes, 0, 4));
        Assert.Equal("WEBP", Encoding.ASCII.GetString(picture.Bytes, 8, 4));
        Assert.Equal("VP8 ", Encoding.ASCII.GetString(picture.Bytes, 12, 4));
        using var decoded = TestImages.Decode(picture.Bytes);
        Assert.Equal((640, 480), (decoded.Width, decoded.Height));
        Assert.True(TestImages.Near(decoded.GetPixel(160, 240), Red));
        Assert.True(TestImages.Near(decoded.GetPixel(480, 240), Blue));
    }

    [Fact]
    public void A_wide_png_is_reduced_whole_not_cropped()
    {
        // 4000x1000, top half red, bottom half blue: all of it is kept, at 2000x500.
        var picture = processor.Process(TestImages.Halves(4000, 1000, Red, Blue, SKEncodedImageFormat.Png, vertical: true));

        Assert.Equal((2000, 500), (picture.Width, picture.Height));
        using var decoded = TestImages.Decode(picture.Bytes);
        Assert.Equal((2000, 500), (decoded.Width, decoded.Height));
        Assert.True(TestImages.Near(decoded.GetPixel(10, 100), Red));
        Assert.True(TestImages.Near(decoded.GetPixel(1990, 400), Blue));
    }

    [Fact]
    public void Rotate_180_is_applied()
    {
        // EXIF 3: the stored left (red) half is seen on the right.
        var photo = TestImages.WithExifOrientation(TestImages.Halves(1200, 800, Red, Blue, SKEncodedImageFormat.Jpeg), orientation: 3);

        var picture = processor.Process(photo);
        using var decoded = TestImages.Decode(picture.Bytes);

        Assert.Equal((1200, 800), (decoded.Width, decoded.Height));
        Assert.True(TestImages.Near(decoded.GetPixel(300, 400), Blue));
        Assert.True(TestImages.Near(decoded.GetPixel(900, 400), Red));
        Assert.DoesNotContain("Exif", Encoding.ASCII.GetString(picture.Bytes));
    }

    [Fact]
    public void Transparency_is_flattened_onto_white()
    {
        var picture = processor.Process(TestImages.TransparentLeftHalf(600, 400, Green));

        Assert.Equal("VP8 ", Encoding.ASCII.GetString(picture.Bytes, 12, 4)); // no alpha chunk
        using var decoded = TestImages.Decode(picture.Bytes);
        Assert.True(TestImages.Near(decoded.GetPixel(150, 200), SKColors.White), $"left is {decoded.GetPixel(150, 200)}");
        Assert.True(TestImages.Near(decoded.GetPixel(450, 200), Green), $"right is {decoded.GetPixel(450, 200)}");
    }

    [Fact]
    public void Refusals_name_the_picture_with_the_covers_codes()
    {
        var gif = Convert.FromBase64String("R0lGODlhAQABAIAAAAAAAP///ywAAAAAAQABAAACAUwAOw==");

        var notAnImage = Assert.Throws<CoverImageException>(() => processor.Process("not an image"u8.ToArray()));
        var wrongFormat = Assert.Throws<CoverImageException>(() => processor.Process(gif));
        var empty = Assert.Throws<CoverImageException>(() => processor.Process([]));

        Assert.Equal((CoverErrorCodes.UnsupportedFormat, "يجب أن تكون الصورة بصيغة JPEG أو PNG أو WebP."), (notAnImage.Code, notAnImage.Message));
        Assert.Equal((CoverErrorCodes.UnsupportedFormat, "يجب أن تكون الصورة بصيغة JPEG أو PNG أو WebP (هذا الملف بصيغة Gif)."), (wrongFormat.Code, wrongFormat.Message));
        Assert.Equal((CoverErrorCodes.UnsupportedFormat, ChapterImages.Refusals.NotAnImage), (empty.Code, empty.Message));
    }

    [Fact]
    public async Task A_file_over_the_limit_is_refused_while_it_is_read()
    {
        using var upload = new MemoryStream(new byte[ChapterImages.MaxUploadBytes + 1]);

        var refused = await Assert.ThrowsAsync<CoverImageException>(() =>
            ImagePipeline.ReadUploadAsync(upload, ChapterImages.MaxUploadBytes, ChapterImages.Refusals, CancellationToken.None));

        Assert.Equal((CoverErrorCodes.FileTooLarge, "يجب ألا يتجاوز حجم ملف الصورة 5 ميغابايت."), (refused.Code, refused.Message));
    }

    [Fact]
    public void The_covers_own_wording_is_unchanged()
    {
        Assert.Equal("يجب أن يكون الغلاف صورة بصيغة JPEG أو PNG أو WebP.", NovelCovers.Refusals.NotAnImage);
        Assert.Equal("يجب أن يكون الغلاف صورة بصيغة JPEG أو PNG أو WebP (هذا الملف بصيغة Gif).", NovelCovers.Refusals.NotAnImageOf("Gif"));
        Assert.Equal("يجب ألا يتجاوز حجم ملف الغلاف 5 ميغابايت.", NovelCovers.Refusals.FileTooLarge);
    }
}
