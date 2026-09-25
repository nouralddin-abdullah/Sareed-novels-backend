using Domain.Entities;
using Domain.Ranking;
using Infrastructure.Persistence;
using Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace Sareed_novels_backend.Tests.Integration;

/// <summary>Builds a small, realistic world once and runs the real ranking against it.</summary>
public class RankingWorld : SqlServerDatabase
{
    public static readonly DateTime Now = new(2026, 9, 24, 12, 0, 0, DateTimeKind.Utc);

    public Genre Genre { get; } = Seed.Genre();
    public Genre EmptyGenre { get; } = Seed.Genre();
    public Novel ActiveReaders { get; private set; } = default!;   // 5 chapters, 3 readers this week, author reading own book
    public Novel ViewSpike { get; private set; } = default!;       // 5 chapters, a 100-view burst 6 days ago, no readers
    public Novel NoChapters { get; private set; } = default!;      // only a draft chapter + gamed 5.0 reviews
    public Novel DraftNovel { get; private set; } = default!;      // draft novel with readers
    public Novel BrandNew { get; private set; } = default!;        // 1 chapter 2 days ago, 1 reader
    public int StaleListId { get; private set; }

    public override async Task InitializeAsync()
    {
        await base.InitializeAsync();
        await using var db = CreateContext();

        var author = Seed.User();
        var readers = Enumerable.Range(0, 5).Select(_ => Seed.User()).ToList();
        db.Users.Add(author);
        db.Users.AddRange(readers);
        db.Genres.AddRange(Genre, EmptyGenre);

        ActiveReaders = Seed.Novel(author, "active");
        ViewSpike = Seed.Novel(author, "spike");
        NoChapters = Seed.Novel(author, "empty");
        DraftNovel = Seed.Novel(author, "draft", isDraft: true);
        BrandNew = Seed.Novel(author, "brand new");
        db.Novels.AddRange(ActiveReaders, ViewSpike, NoChapters, DraftNovel, BrandNew);
        foreach (var novel in new[] { ActiveReaders, ViewSpike, NoChapters, DraftNovel, BrandNew })
        {
            db.NovelGenres.Add(new NovelGenre { NovelId = novel.Id, Genre = Genre });
        }

        var activeChapters = Seed.Chapters(ActiveReaders, 5, Now.AddDays(-100));
        var spikeChapters = Seed.Chapters(ViewSpike, 5, Now.AddDays(-100));
        var draftNovelChapters = Seed.Chapters(DraftNovel, 5, Now.AddDays(-100));
        var newChapters = Seed.Chapters(BrandNew, 1, Now.AddDays(-2));
        var onlyDraftChapter = Seed.Chapters(NoChapters, 1, Now.AddDays(-5), status: "Draft");
        db.Chapters.AddRange(activeChapters.Concat(spikeChapters).Concat(draftNovelChapters).Concat(newChapters).Concat(onlyDraftChapter));

        // Three real readers this week; the author's own progress must not count.
        db.UserNovelProgress.AddRange(
            Seed.Progress(readers[0], activeChapters[4], 5, Now.AddDays(-1)),
            Seed.Progress(readers[1], activeChapters[3], 4, Now.AddDays(-2)),
            Seed.Progress(readers[2], activeChapters[2], 3, Now.AddDays(-3)),
            Seed.Progress(author, activeChapters[4], 5, Now.AddHours(-1)),
            Seed.Progress(readers[3], draftNovelChapters[0], 1, Now.AddDays(-1)),
            Seed.Progress(readers[4], newChapters[0], 1, Now.AddDays(-1)));

        db.NovelViews.Add(new NovelViews { NovelId = ViewSpike.Id, ViewDate = Now.Date.AddDays(-6), ViewCount = 100 });

        // Trusted review (reader reached chapter 5) vs. gamed reviews on a novel nobody could read.
        db.Reviews.Add(Review(readers[0], ActiveReaders, 4.5m));
        db.Reviews.AddRange(readers.Take(3).Select(r => Review(r, NoChapters, 5m)));

        // A stale list from the old job: must be emptied, not left showing old entries.
        var stale = new RankingList { Genre = EmptyGenre, RankingType = RankingTypes.Trending, Name = "TrendingOld", TotalNovels = 1 };
        db.RankingLists.Add(stale);
        await db.SaveChangesAsync();
        db.RankingEntries.Add(new RankingEntry { RankingListId = stale.Id, NovelId = NoChapters.Id, Rank = 1 });
        await db.SaveChangesAsync();
        StaleListId = stale.Id;

        await new RankingService(db, new FixedTimeProvider(Now), NullLogger<RankingService>.Instance).CalculateAllGenreRankings();
    }

    private static Review Review(User reviewer, Novel novel, decimal score) => new()
    {
        Id = Guid.NewGuid(),
        ReviewerId = reviewer.Id,
        NovelId = novel.Id,
        WritingQualityScore = score,
        UpdatingStabilityScore = score,
        CharacterDevelopmentScore = score,
        WorldBuildingScore = score,
        TotalAverageScore = score,
        Content = "!"
    };

    public async Task<List<RankingEntry>> Entries(int? genreId, string type)
    {
        await using var db = CreateContext();
        return await db.RankingEntries
            .Where(e => e.RankingList.GenreId == genreId && e.RankingList.RankingType == type)
            .OrderBy(e => e.Rank)
            .ToListAsync();
    }
}

public class RankingServiceTests(RankingWorld world) : IClassFixture<RankingWorld>
{
    [Fact]
    public async Task Trending_puts_real_readers_first_and_skips_drafts_and_empty_novels()
    {
        var trending = (await world.Entries(null, RankingTypes.Trending)).Select(e => e.NovelId).ToList();

        Assert.Equal(world.ActiveReaders.Id, trending[0]);
        Assert.Contains(world.ViewSpike.Id, trending);
        Assert.Contains(world.BrandNew.Id, trending);
        Assert.DoesNotContain(world.NoChapters.Id, trending);
        Assert.DoesNotContain(world.DraftNovel.Id, trending);
    }

    [Fact]
    public async Task The_authors_own_reading_is_not_counted()
    {
        var entry = (await world.Entries(null, RankingTypes.Trending)).Single(e => e.NovelId == world.ActiveReaders.Id);
        Assert.Equal(3, entry.PopularityScore); // 3 readers, not 4
    }

    [Fact]
    public async Task All_time_needs_readers_and_at_least_three_chapters()
    {
        var allTime = (await world.Entries(null, RankingTypes.AllTime)).Select(e => e.NovelId).ToList();

        Assert.Equal(new[] { world.ActiveReaders.Id }, allTime);
    }

    [Fact]
    public async Task New_list_only_has_recent_novels()
    {
        var newList = (await world.Entries(world.Genre.Id, RankingTypes.New)).Select(e => e.NovelId).ToList();

        Assert.Equal(new[] { world.BrandNew.Id }, newList);
    }

    [Fact]
    public async Task Stale_lists_are_emptied()
    {
        await using var db = world.CreateContext();
        var stale = await db.RankingLists.Include(l => l.Entries).SingleAsync(l => l.Id == world.StaleListId);

        Assert.Empty(stale.Entries);
        Assert.Equal(0, stale.TotalNovels);
        Assert.Equal(RankingWorld.Now, stale.LastUpdated);
    }

    [Fact]
    public async Task Only_reviews_from_readers_count_toward_quality()
    {
        var topRated = await world.Entries(world.Genre.Id, RankingTypes.TopRated);

        // The reader's 4.5 is the only trusted rating, so it is also the site-wide prior and the stored quality.
        // The three 5.0 reviews on a novel with no published chapters are ignored (and that novel isn't ranked).
        Assert.DoesNotContain(topRated, e => e.NovelId == world.NoChapters.Id);
        Assert.Equal(4.5m, topRated.Single(e => e.NovelId == world.ActiveReaders.Id).QualityScore);
    }
}
