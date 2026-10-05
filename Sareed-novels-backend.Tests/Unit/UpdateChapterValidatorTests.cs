using Application.Chapters.Commands.UpdateChapter;

namespace Sareed_novels_backend.Tests.Unit;

/// <summary>
/// #75: a chapter's status can be changed alone, without its title and text; when either is sent both are needed and
/// keep their limits (the text's counted as readers see it, #74).
/// </summary>
public class UpdateChapterValidatorTests
{
    private static readonly UpdateChapterValidator Validator = new();

    private static List<string> Errors(string? title, string? content, string? status, int? baseRevision = null) =>
        Validator.Validate(new UpdateChapterRequest { Title = title, Content = content, Status = status, BaseRevision = baseRevision })
            .Errors.Select(e => e.ErrorMessage).ToList();

    [Theory]
    [InlineData("Published")]
    [InlineData("Draft")]
    public void A_status_alone_is_a_save(string status)
    {
        Assert.Empty(Errors(null, null, status));
        Assert.Empty(Errors(null, null, status, baseRevision: 3));
    }

    [Fact]
    public void Nothing_to_save_is_refused()
    {
        Assert.Equal([UpdateChapterValidator.StatusMissingMessage], Errors(null, null, null));
    }

    [Fact]
    public void A_title_needs_the_text_and_the_text_a_title()
    {
        Assert.Equal(["اكتب نص الفصل"], Errors("فصل", null, null));
        Assert.Equal(["اكتب عنوان الفصل"], Errors(null, "<p>نص</p>", "Published"));
        Assert.Empty(Errors("فصل", "<p>نص</p>", null));
        Assert.Empty(Errors("فصل", "", null));
    }

    [Fact]
    public void The_limits_hold_when_the_title_and_text_are_sent()
    {
        Assert.Equal(["يجب ألا يتجاوز عنوان الفصل 50 حرفًا"], Errors(new string('ع', 51), "<p>نص</p>", null));
        Assert.Equal(["يجب ألا يتجاوز نص الفصل 100000 حرف"], Errors("فصل", new string('ن', 100_001), null));
        Assert.Equal(["يجب ألا يتجاوز نص الفصل مع تنسيقه 400000 حرف"], Errors("فصل", "<b>" + new string('ن', 400_000) + "</b>", null));
        Assert.Equal(["حالة الفصل يجب أن تكون «مسودة» أو «منشور»"], Errors(null, null, "NotAStatus"));
    }
}
