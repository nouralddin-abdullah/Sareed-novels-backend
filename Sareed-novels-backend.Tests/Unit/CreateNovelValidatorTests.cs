using Application.Novels.Commands.CreateNovel;
using Microsoft.AspNetCore.Http;

namespace Sareed_novels_backend.Tests.Unit;

public class CreateNovelValidatorTests
{
    private static readonly CreateNovelCommandValidator Validator = new();

    private static IFormFile Image(string contentType, long length = 1024) =>
        new FormFile(new MemoryStream(new byte[length]), 0, length, "CoverImageUrl", "cover")
        {
            Headers = new HeaderDictionary(),
            ContentType = contentType
        };

    private static CreateNovelCommand Command(IFormFile? cover) => new()
    {
        Title = "A novel title",
        Summary = "A summary that is long enough.",
        CoverImageUrl = cover!,
        GenreIds = [1]
    };

    [Fact]
    public void A_novel_without_a_cover_is_rejected_with_a_clear_message()
    {
        var result = Validator.Validate(Command(null));

        var error = Assert.Single(result.Errors);
        Assert.Equal(nameof(CreateNovelCommand.CoverImageUrl), error.PropertyName);
        Assert.Equal(CreateNovelCommandValidator.CoverRequiredMessage, error.ErrorMessage);
    }

    [Theory]
    [InlineData("image/jpeg")]
    [InlineData("image/png")]
    [InlineData("image/webp")]
    public void Jpeg_png_and_webp_covers_up_to_5_mb_are_accepted(string contentType)
    {
        Assert.True(Validator.Validate(Command(Image(contentType, 5 * 1024 * 1024))).IsValid);
    }

    [Theory]
    [InlineData("image/gif", 1024)]
    [InlineData("image/png", 5 * 1024 * 1024 + 1)]
    public void Other_types_and_larger_files_are_rejected(string contentType, long length)
    {
        var error = Assert.Single(Validator.Validate(Command(Image(contentType, length))).Errors);
        Assert.Equal(CreateNovelCommandValidator.CoverInvalidMessage, error.ErrorMessage);
    }
}
