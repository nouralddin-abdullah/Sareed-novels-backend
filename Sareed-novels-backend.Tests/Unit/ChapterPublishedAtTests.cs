using Domain.Constants;
using Domain.Entities;

namespace Sareed_novels_backend.Tests.Unit;

/// <summary>
/// #33: a chapter's PublishedAt is when it first came out. Publishing stamps it the first time only; drafts that were
/// never published have none, and unpublishing or publishing again never moves it.
/// </summary>
public class ChapterPublishedAtTests
{
    private static readonly DateTime Written = new(2026, 9, 1, 8, 0, 0, DateTimeKind.Utc);

    private static Chapter NewChapter() => new() { Id = Guid.NewGuid(), Title = "فصل", Slug = "s", CreatedAt = Written };

    [Fact]
    public void Publishing_stamps_the_time_it_comes_out()
    {
        var chapter = NewChapter();

        chapter.SetStatus(ChapterStatuses.Published, Written);

        Assert.Equal((ChapterStatuses.Published, Written), (chapter.Status, chapter.PublishedAt));
    }

    [Fact]
    public void A_draft_is_not_stamped()
    {
        var chapter = NewChapter();

        chapter.SetStatus(ChapterStatuses.Draft, Written);

        Assert.Equal(ChapterStatuses.Draft, chapter.Status);
        Assert.Null(chapter.PublishedAt);
    }

    [Fact]
    public void A_draft_published_later_is_stamped_when_published_not_when_written()
    {
        var chapter = NewChapter();
        chapter.SetStatus(ChapterStatuses.Draft, Written);

        chapter.SetStatus(ChapterStatuses.Published, Written.AddDays(3));

        Assert.Equal(Written.AddDays(3), chapter.PublishedAt);
    }

    [Fact]
    public void Unpublishing_and_publishing_again_keep_the_first_time()
    {
        var chapter = NewChapter();
        chapter.SetStatus(ChapterStatuses.Published, Written.AddDays(1));

        chapter.SetStatus(ChapterStatuses.Draft, Written.AddDays(2));
        Assert.Equal((ChapterStatuses.Draft, Written.AddDays(1)), (chapter.Status, chapter.PublishedAt));

        chapter.SetStatus(ChapterStatuses.Published, Written.AddDays(5));
        Assert.Equal((ChapterStatuses.Published, Written.AddDays(1)), (chapter.Status, chapter.PublishedAt));

        // The editor sends the status with every save: saving a published chapter again changes nothing either.
        chapter.SetStatus(ChapterStatuses.Published, Written.AddDays(6));
        Assert.Equal(Written.AddDays(1), chapter.PublishedAt);
    }
}
