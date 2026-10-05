using Application.Chapters.Commands.UpdateChapter;

namespace Sareed_novels_backend.Tests.Unit;

/// <summary>
/// #75: a chapter's status can be changed alone, without its title and text; #77: so can its schedule (publishAt, a
/// time or null), alone or with the status. When the title or the text is sent both are needed and keep their limits
/// (the text's counted as readers see it, #74).
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
        Assert.Equal([UpdateChapterValidator.StatusMissingMessage], Errors(null, null, null, baseRevision: 3));
        Assert.Equal("أرسل حالة الفصل أو موعد نشره، أو عنوانه ونصه", UpdateChapterValidator.StatusMissingMessage);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("Draft")]
    [InlineData("Published")]
    public void A_schedule_set_or_cancelled_is_a_save_alone_or_with_a_status(string? status)
    {
        var at = new DateTime(2026, 10, 6, 8, 0, 0, DateTimeKind.Utc);
        Assert.Empty(Scheduled(new UpdateChapterRequest { Status = status, PublishAt = at }));
        Assert.Empty(Scheduled(new UpdateChapterRequest { Status = status, PublishAt = null }));
        Assert.Empty(Scheduled(new UpdateChapterRequest { Status = status, PublishAt = at, BaseRevision = 3 }));
    }

    [Fact]
    public void A_schedule_with_a_title_or_the_text_still_needs_both()
    {
        var at = new DateTime(2026, 10, 6, 8, 0, 0, DateTimeKind.Utc);
        Assert.Equal(["اكتب نص الفصل"], Scheduled(new UpdateChapterRequest { Title = "فصل", PublishAt = at }));
        Assert.Equal(["اكتب عنوان الفصل"], Scheduled(new UpdateChapterRequest { Content = "<p>نص</p>", PublishAt = null }));
        Assert.Empty(Scheduled(new UpdateChapterRequest { Title = "فصل", Content = "<p>نص</p>", PublishAt = at }));
    }

    private static List<string> Scheduled(UpdateChapterRequest request) =>
        Validator.Validate(request).Errors.Select(e => e.ErrorMessage).ToList();

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
