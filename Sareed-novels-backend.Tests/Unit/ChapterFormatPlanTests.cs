using Application.Chapters.Commands.CleanChapterFormat;
using Application.Chapters.Paragraphs;
using Domain.Constants;
using Domain.Entities;
using static Application.Chapters.Commands.CleanChapterFormat.ChapterFormatPlan;

namespace Sareed_novels_backend.Tests.Unit;

/// <summary>What the format maintenance (#74) does to a chapter's stored paragraphs, decided before any write.</summary>
public class ChapterFormatPlanTests
{
    private static readonly Guid ChapterId = Guid.NewGuid();
    private static readonly DateTime Created = new(2025, 11, 2, 10, 0, 0, DateTimeKind.Utc);

    private static ChapterParagraph Row(int index, string content, string kind = ParagraphKinds.Text, string? hash = null,
        string? caption = null) => new()
    {
        Id = Guid.NewGuid(), ChapterId = ChapterId, Content = content, ContentType = kind, Caption = caption,
        ContentHash = hash ?? ParagraphText.Hash(content), OrderIndex = index, CreatedAt = Created
    };

    private static List<Change> Changes(ChapterFormatPlan plan) => plan.Rows.Select(r => r.Change).ToList();

    [Fact]
    public void Each_stored_paragraph_gets_the_change_it_needs()
    {
        var plan = For(
        [
            Row(0, "<p class=\"min-h-[1em]\">نص"),
            Row(1, "نص"),
            Row(2, "نص", hash: "stale"),
            Row(3, "https://files.test/a.png", ParagraphKinds.Image, caption: "  تعليق "),
            Row(4, "* * *", ParagraphKinds.Break),
            Row(5, "<p></p>"),
            Row(6, "قبل<img src=\"https://files.test/a.png\">بعد"),
            Row(7, "نص", "paragraph")
        ]);

        Assert.Equal(
            [Change.Rewrite, Change.None, Change.HashFix, Change.Rewrite, Change.None, Change.Remove, Change.Split, Change.Rewrite],
            Changes(plan));
        Assert.True(plan.HasChanges);
    }

    [Theory]
    [InlineData("<p>أ<script>alert(1)</script>ب</p>", VisibleTextChanged)]
    [InlineData("<p>أ<textarea>نص</textarea>ب</p>", VisibleTextChanged)]
    [InlineData("<p>قبل<!-- ملاحظة -->بعد</p>", VisibleTextChanged)]
    [InlineData("x<y and z", VisibleTextChanged)]
    [InlineData("<p>صورة <img src=\"data:image/png;base64,AAAA\"> هنا</p>", PictureDropped)]
    [InlineData("<p><img src=\"/local/a.png\"></p>", PictureDropped)]
    public void A_paragraph_whose_words_or_picture_the_cleaning_would_lose_is_left_alone(string content, string reason)
    {
        var row = Assert.Single(For([Row(0, content)]).Rows);

        Assert.Equal((Change.Skip, reason), (row.Change, row.SkipReason));
    }

    [Fact]
    public void A_stored_picture_without_an_http_address_is_left_alone()
    {
        var row = Assert.Single(For([Row(0, "javascript:alert(1)", ParagraphKinds.Image)]).Rows);

        Assert.Equal((Change.Skip, PictureDropped), (row.Change, row.SkipReason));
        Assert.Equal((1, 0), (row.Conversion.Pictures, row.Conversion.PicturesKept));
    }

    [Fact]
    public void An_empty_paragraph_with_comments_stays()
    {
        var commented = Row(1, "<p><br></p>");
        var plan = For([Row(0, "نص"), commented, Row(2, "<p class=\"min-h-[1em]\">")]);
        Assert.Equal([commented.Id, plan.Rows[2].Paragraph.Id], plan.EmptyRows);

        plan.KeepCommented(new HashSet<Guid> { commented.Id });

        Assert.Equal([Change.None, Change.Skip, Change.Remove], Changes(plan));
        Assert.Equal(EmptyWithComments, plan.Rows[1].SkipReason);
    }

    [Fact]
    public void A_split_paragraph_keeps_its_id_on_its_first_text_and_the_chapter_moves_up_behind_it()
    {
        var before = Row(0, "<p class=\"min-h-[1em]\">أول");
        var picture = Row(1, "<p class=\"min-h-[1em]\"><img src=\"https://files.test/a.png\"> بعد الصورة <img src=\"https://files.test/b.png\">");
        var empty = Row(2, "<p></p>");
        var after = Row(3, "أخير");
        var plan = For([before, picture, empty, after]);

        var (paragraphs, removed) = plan.Apply();

        Assert.Equal([empty], removed);
        Assert.Equal(
        [
            (before.Id, ParagraphKinds.Text, "أول"),
            (paragraphs[1].Id, ParagraphKinds.Image, "https://files.test/a.png"),
            (picture.Id, ParagraphKinds.Text, "بعد الصورة"),
            (paragraphs[3].Id, ParagraphKinds.Image, "https://files.test/b.png"),
            (after.Id, ParagraphKinds.Text, "أخير")
        ], paragraphs.Select(p => (p.Id, p.ContentType, p.Content)));
        Assert.Equal(Enumerable.Range(0, 5), paragraphs.Select(p => p.OrderIndex));
        Assert.All(paragraphs, p => Assert.Equal(ParagraphText.Hash(p.Content), p.ContentHash));
        Assert.All(paragraphs, p => Assert.Equal((ChapterId, Created), (p.ChapterId, p.CreatedAt)));
        Assert.DoesNotContain(paragraphs, p => p.UpdatedAt != null);

        // The report shows the paragraph as it was stored, not as Apply left it.
        Assert.Equal(picture.Id, plan.Rows[1].Paragraph.Id);
        Assert.StartsWith("<p class=\"min-h-[1em]\"><img", plan.Rows[1].Stored.Content);
    }

    [Fact]
    public void A_chapter_already_in_the_format_is_left_as_it_is()
    {
        var rows = ChapterFormat.Parse("<p>أ</p><p data-kind=\"center\">ب</p><hr><p data-kind=\"image\"><img src=\"https://files.test/a.png\">ج</p>")
            .Select((p, i) => Row(i, p.Content, p.Kind, caption: p.Caption))
            .ToList();

        var plan = For(rows);

        Assert.All(plan.Rows, r => Assert.Equal(Change.None, r.Change));
        Assert.False(plan.HasChanges);
    }

    [Fact]
    public void Converted_paragraphs_convert_to_themselves()
    {
        var rows = new List<ChapterParagraph>
        {
            Row(0, "<p class=\"min-h-[1em]\"><b>أ</b>&nbsp; <i>ب</i>"),
            Row(1, "قبل <img src=\"https://files.test/a.png\"> بعد"),
            Row(2, "HTTPS://Files.Test/B.png", ParagraphKinds.Image, caption: "تعليق"),
            Row(3, "<p>   </p>"),
            Row(4, "---", ParagraphKinds.Break)
        };

        var (converted, _) = For(rows).Apply();
        var again = For(converted);

        Assert.All(again.Rows, r => Assert.Equal(Change.None, r.Change));
    }
}
