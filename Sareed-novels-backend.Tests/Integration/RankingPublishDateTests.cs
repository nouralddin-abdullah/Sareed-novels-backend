using Domain.Constants;
using Domain.Entities;
using Domain.Ranking;
using Infrastructure.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Sareed_novels_backend.Tests.Integration;

/// <summary>
/// #39: the rankings place a novel in time by when its published chapters came out (Chapters.PublishedAt), not when
/// they were written: Trending's fresh-chapter boost and the tie-break by the newest chapter, and the New lists'
/// 60-day window and head start by the first. Drafts and unpublished chapters don't count, and publishing a chapter
/// again doesn't make it new. The chapters are written, published and unpublished through the real handlers, on a
/// clock.
/// </summary>
public class RankingPublishDateTests(SqlServerDatabase database) : IClassFixture<SqlServerDatabase>
{
    private static readonly DateTime Now = new(2026, 9, 24, 12, 0, 0, DateTimeKind.Utc);

    private readonly ChapterDesk desk = new(database, Now.AddDays(-100));

    [Fact]
    public async Task A_draft_written_before_the_new_window_and_published_inside_it_counts_from_its_publish_date()
    {
        var (author, novel, genre) = await NovelInGenre();
        var chapter = await At(Now.AddDays(-100), () => desk.Create(author, novel, ChapterStatuses.Draft));
        await At(Now.AddDays(-1), () => desk.Save(author, novel, chapter, ChapterStatuses.Published));

        var lists = await Rank();

        // Out yesterday, written 100 days ago: new, and as fresh as a chapter out yesterday.
        var cameOut = Dated(novel, chapters: 1, first: Now.AddDays(-1), last: Now.AddDays(-1));
        Assert.Equal(RankingFormula.Trending(cameOut, Now), Trending(lists, genre, novel), 9);
        Assert.Equal(RankingFormula.New(cameOut, Now), Entry(lists, genre, RankingTypes.New, novel).Score, 9);
    }

    [Fact]
    public async Task Drafts_never_count()
    {
        var (author, novel, genre) = await NovelInGenre();
        await At(Now.AddDays(-100), () => desk.Create(author, novel, ChapterStatuses.Published));
        await At(Now.AddDays(-1), () => desk.Create(author, novel, ChapterStatuses.Draft));
        var (draftAuthor, draftOnly, draftGenre) = await NovelInGenre();
        await At(Now.AddDays(-1), () => desk.Create(draftAuthor, draftOnly, ChapterStatuses.Draft));

        var lists = await Rank();

        // Yesterday's draft neither freshens the novel nor makes it new.
        var old = Dated(novel, chapters: 1, first: Now.AddDays(-100), last: Now.AddDays(-100));
        Assert.Equal(RankingFormula.Trending(old, Now), Trending(lists, genre, novel), 9);
        Assert.DoesNotContain(List(lists, genre, RankingTypes.New), e => e.NovelId == novel.Id);
        // A novel with only a draft isn't ranked at all.
        Assert.DoesNotContain(lists.SelectMany(l => l.Entries), e => e.NovelId == draftOnly.Id);
        Assert.All(lists.Where(l => l.GenreId == draftGenre.Id), l => Assert.Empty(l.Entries));
    }

    [Fact]
    public async Task Publishing_a_chapter_again_does_not_make_it_new_or_fresh_and_unpublished_chapters_do_not_count()
    {
        var (author, republished, genre) = await NovelInGenre();
        var chapter = await At(Now.AddDays(-100), () => desk.Create(author, republished, ChapterStatuses.Published));
        await At(Now.AddDays(-2), () => desk.Save(author, republished, chapter, ChapterStatuses.Draft));
        await At(Now.AddDays(-1), () => desk.Save(author, republished, chapter, ChapterStatuses.Published));

        var (otherAuthor, unpublished, otherGenre) = await NovelInGenre();
        await At(Now.AddDays(-100), () => desk.Create(otherAuthor, unpublished, ChapterStatuses.Published));
        var withdrawn = await At(Now.AddDays(-3),
            () => desk.Create(otherAuthor, unpublished, ChapterStatuses.Published));
        await At(Now.AddDays(-2), () => desk.Save(otherAuthor, unpublished, withdrawn, ChapterStatuses.Draft));

        var lists = await Rank();

        // Published again yesterday, out since 100 days ago: counted from then.
        var firstOut = Dated(republished, chapters: 1, first: Now.AddDays(-100), last: Now.AddDays(-100));
        Assert.Equal(RankingFormula.Trending(firstOut, Now), Trending(lists, genre, republished), 9);
        Assert.DoesNotContain(List(lists, genre, RankingTypes.New), e => e.NovelId == republished.Id);

        // Out three days ago and unpublished since: while it isn't published it doesn't freshen the novel.
        var onlyTheOld = Dated(unpublished, chapters: 1, first: Now.AddDays(-100), last: Now.AddDays(-100));
        Assert.Equal(RankingFormula.Trending(onlyTheOld, Now), Trending(lists, otherGenre, unpublished), 9);
        Assert.DoesNotContain(List(lists, otherGenre, RankingTypes.New), e => e.NovelId == unpublished.Id);
    }

    [Fact]
    public async Task Novels_that_tie_are_ordered_by_when_their_newest_chapter_came_out()
    {
        // Two novels no one has read or rated yet tie on Top Rated; the one whose newest chapter came out last goes
        // first. Its chapters were written 50 days ago and the last came out yesterday; the other's 10 days ago.
        var genre = await NewGenre();
        var (author, outYesterday, _) = await NovelInGenre(genre);
        var drafts = new List<Guid>();
        for (var i = 0; i < 3; i++)
        {
            drafts.Add(await At(Now.AddDays(-50), () => desk.Create(author, outYesterday, ChapterStatuses.Draft)));
        }
        await At(Now.AddDays(-49), () => desk.Save(author, outYesterday, drafts[0], ChapterStatuses.Published));
        await At(Now.AddDays(-49), () => desk.Save(author, outYesterday, drafts[1], ChapterStatuses.Published));
        await At(Now.AddDays(-1), () => desk.Save(author, outYesterday, drafts[2], ChapterStatuses.Published));

        var (otherAuthor, outTenDaysAgo, _) = await NovelInGenre(genre);
        for (var i = 0; i < 3; i++)
        {
            await At(Now.AddDays(-10), () => desk.Create(otherAuthor, outTenDaysAgo, ChapterStatuses.Published));
        }

        var topRated = List(await Rank(), genre, RankingTypes.TopRated);

        Assert.Equal(topRated[0].Score, topRated[1].Score);
        Assert.Equal([outYesterday.Id, outTenDaysAgo.Id], topRated.Select(e => e.NovelId));
    }

    [Fact]
    public async Task A_published_chapter_without_a_publish_date_is_not_guessed_and_the_log_says_so()
    {
        // Only code from before PublishedAt could publish one (a deploy overlap or a rollback), as seeded here.
        var (_, placedByTheOther, genre) = await NovelInGenre();
        var (_, undatedOnly, otherGenre) = await NovelInGenre();
        await using (var db = database.CreateContext())
        {
            var dated = Seed.Chapters(placedByTheOther, 1, Now.AddDays(-10)).Single();
            var undated = Seed.Chapters(placedByTheOther, 1, Now.AddDays(-1), startIndex: 2).Single();
            var alone = Seed.Chapters(undatedOnly, 1, Now.AddDays(-1)).Single();
            undated.PublishedAt = alone.PublishedAt = null;
            db.Chapters.AddRange(dated, undated, alone);
            await db.SaveChangesAsync();
        }
        var logger = new ListLogger<RankingService>();

        IReadOnlyList<RankingService.ComputedList> lists;
        await using (var db = database.CreateContext())
        {
            lists = await new RankingService(db, new FixedTimeProvider(Now), logger).ComputeListsAsync(Now);
        }

        // Placed by the chapter whose date is known; left out while none is known.
        var datedOut = Now.AddDays(-10).AddMinutes(1);
        var byTheDated = Dated(placedByTheOther, chapters: 2, first: datedOut, last: datedOut);
        Assert.Equal(RankingFormula.Trending(byTheDated, Now), Trending(lists, genre, placedByTheOther), 9);
        Assert.Empty(List(lists, otherGenre, RankingTypes.Trending));
        var error = Assert.Single(logger.Entries, e => e.Level == LogLevel.Error);
        Assert.Equal(
            new[] { placedByTheOther.Id, undatedOnly.Id }.Order(),
            ((IEnumerable<Guid>)error.Values["NovelIds"]!).Order());
    }

    /// <summary>A public novel of a new author in <paramref name="genre"/> (a genre of its own by default).</summary>
    private async Task<(User Author, Novel Novel, Genre Genre)> NovelInGenre(Genre? genre = null)
    {
        genre ??= await NewGenre();
        var (author, novel) = await desk.SeedNovel();
        await using var db = database.CreateContext();
        db.NovelGenres.Add(new NovelGenre { NovelId = novel.Id, GenreId = genre.Id });
        await db.SaveChangesAsync();
        return (author, novel, genre);
    }

    private async Task<Genre> NewGenre()
    {
        await using var db = database.CreateContext();
        var genre = Seed.Genre();
        db.Genres.Add(genre);
        await db.SaveChangesAsync();
        return genre;
    }

    private async Task<T> At<T>(DateTime time, Func<Task<T>> write)
    {
        desk.Clock.UtcNow = time;
        return await write();
    }

    private async Task At(DateTime time, Func<Task> write)
    {
        desk.Clock.UtcNow = time;
        await write();
    }

    private async Task<IReadOnlyList<RankingService.ComputedList>> Rank()
    {
        await using var db = database.CreateContext();
        var rankings = new RankingService(db, new FixedTimeProvider(Now), NullLogger<RankingService>.Instance);
        return await rankings.ComputeListsAsync(Now);
    }

    /// <summary>What the ranking should know about a novel with no readers, views, comments or reviews.</summary>
    private static NovelSignals Dated(Novel novel, int chapters, DateTime first, DateTime last) => new()
    {
        NovelId = novel.Id, PublishedChapters = chapters, FirstChapterAt = first, LastChapterAt = last
    };

    private static List<RankingService.ComputedEntry> List(
        IEnumerable<RankingService.ComputedList> lists, Genre genre, string type) =>
        lists.Single(l => l.GenreId == genre.Id && l.Type == type).Entries;

    private static RankingService.ComputedEntry Entry(
        IEnumerable<RankingService.ComputedList> lists, Genre genre, string type, Novel novel) =>
        Assert.Single(List(lists, genre, type), e => e.NovelId == novel.Id);

    /// <summary>The novel's Trending score in its genre's Trending list.</summary>
    private static double Trending(IEnumerable<RankingService.ComputedList> lists, Genre genre, Novel novel) =>
        Entry(lists, genre, RankingTypes.Trending, novel).Trending;
}
