using Domain.Library;

namespace Sareed_novels_backend.Tests.Unit;

public class ReadingPositionTests
{
    private static ChapterOutline Chapter(int index) => new(Guid.NewGuid(), $"Chapter {index}", index);

    [Fact]
    public void A_published_last_read_chapter_is_numbered_by_its_place_among_published_chapters()
    {
        // Index 2 is a draft, so the chapter at index 4 is the 3rd published chapter.
        var published = new[] { Chapter(1), Chapter(3), Chapter(4), Chapter(6) };

        var resume = ReadingPosition.Resolve(published[2], published);

        Assert.Equal(published[2].Id, resume.ChapterId);
        Assert.Equal(3, resume.ChapterNumber);
        Assert.Equal(4, resume.PublishedChapters);
        Assert.Equal(75m, resume.ProgressPercentage);
    }

    [Fact]
    public void An_unpublished_last_read_chapter_resumes_at_the_nearest_published_chapter_before_it()
    {
        var published = new[] { Chapter(1), Chapter(2), Chapter(5) };
        var unpublished = Chapter(4);

        var resume = ReadingPosition.Resolve(unpublished, published);

        Assert.Equal(published[1].Id, resume.ChapterId);
        Assert.Equal("Chapter 2", resume.ChapterTitle);
        Assert.Equal(2, resume.ChapterNumber);
    }

    [Fact]
    public void With_nothing_published_before_it_the_reader_resumes_at_the_first_chapter()
    {
        var published = new[] { Chapter(5), Chapter(6) };

        var resume = ReadingPosition.Resolve(Chapter(1), published);

        Assert.Equal(published[0].Id, resume.ChapterId);
        Assert.Equal(1, resume.ChapterNumber);
    }

    [Fact]
    public void With_nothing_published_the_stored_chapter_is_kept_at_zero_percent()
    {
        var lastRead = Chapter(3);

        var resume = ReadingPosition.Resolve(lastRead, []);

        Assert.Equal(lastRead.Id, resume.ChapterId);
        Assert.Equal(0, resume.ChapterNumber);
        Assert.Equal(0, resume.PublishedChapters);
        Assert.Equal(0m, resume.ProgressPercentage);
    }

    [Fact]
    public void Progress_never_exceeds_100_percent_after_later_chapters_are_unpublished()
    {
        // The reader reached chapter 10; the author then unpublished chapters 6-10.
        var published = Enumerable.Range(1, 5).Select(Chapter).ToList();

        var resume = ReadingPosition.Resolve(Chapter(10), published);

        Assert.Equal(5, resume.ChapterNumber);
        Assert.Equal(100m, resume.ProgressPercentage);
    }

    [Fact]
    public void The_percentage_is_rounded_to_one_decimal()
    {
        var published = Enumerable.Range(1, 3).Select(Chapter).ToList();

        Assert.Equal(33.3m, ReadingPosition.Resolve(published[0], published).ProgressPercentage);
    }
}
