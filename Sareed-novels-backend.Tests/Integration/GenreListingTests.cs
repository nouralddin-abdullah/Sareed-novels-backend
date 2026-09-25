using Application.Novels.DTOS;
using Application.Novels.Queries.GetPopularByGenre;
using Application.Rankings.DTO;
using AutoMapper;
using Domain.Entities;
using Domain.Exceptions;
using Infrastructure.Persistence;
using Infrastructure.Repositories;
using Infrastructure.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace Sareed_novels_backend.Tests.Integration;

/// <summary>A genre with every kind of novel the genre page must show or hide, plus computed rankings.</summary>
public class GenreWorld : SqlServerDatabase
{
    public static readonly DateTime Now = new(2026, 9, 24, 12, 0, 0, DateTimeKind.Utc);

    public Genre Genre { get; } = Seed.Genre();
    public Genre OtherGenre { get; } = Seed.Genre();
    public Novel Ongoing { get; private set; } = default!;      // also in OtherGenre
    public Novel Completed { get; private set; } = default!;
    public List<Novel> Ties { get; } = [];                      // same views, reviews and score
    public Novel Draft { get; private set; } = default!;
    public Novel NoPublishedChapters { get; private set; } = default!;
    public Novel Ineligible { get; private set; } = default!;
    public Novel Deleted { get; private set; } = default!;

    public IEnumerable<Novel> Visible => new[] { Ongoing, Completed }.Concat(Ties);

    public override async Task InitializeAsync()
    {
        await base.InitializeAsync();
        await using var db = CreateContext();

        var author = Seed.User();
        db.Users.Add(author);
        db.Genres.AddRange(Genre, OtherGenre);

        Ongoing = Seed.Novel(author, "ongoing", createdAt: Now.AddDays(-20));
        Ongoing.TotalViews = 50;
        Completed = Seed.Novel(author, "completed", createdAt: Now.AddDays(-30));
        Completed.Status = "Completed";
        Completed.TotalViews = 40;
        Completed.ReviewCount = 2;
        Completed.TotalAverageScore = 4.5m;
        for (var i = 0; i < 3; i++)
        {
            var tie = Seed.Novel(author, $"tie {i}", createdAt: Now.AddDays(-10));
            tie.TotalViews = 10;
            Ties.Add(tie);
        }

        Draft = Seed.Novel(author, "draft", isDraft: true);
        NoPublishedChapters = Seed.Novel(author, "empty");
        Ineligible = Seed.Novel(author, "ineligible");
        Ineligible.IsEligibleForRanking = false;
        Deleted = Seed.Novel(author, "deleted");
        Deleted.IsDeleted = true;
        foreach (var hidden in new[] { Draft, Ineligible, Deleted })
        {
            hidden.TotalViews = 1000; // would top "popular" if it leaked through
        }

        var all = Visible.Concat([Draft, Ineligible, Deleted]).ToList();
        db.Novels.AddRange(all);
        db.NovelGenres.AddRange(all.Select(n => new NovelGenre { NovelId = n.Id, Genre = Genre }));
        db.NovelGenres.Add(new NovelGenre { NovelId = Ongoing.Id, Genre = OtherGenre });

        // Only in OtherGenre, so the counts above stay about Genre.
        db.Novels.Add(NoPublishedChapters);
        db.NovelGenres.Add(new NovelGenre { NovelId = NoPublishedChapters.Id, Genre = OtherGenre });

        foreach (var novel in all.Where(n => n != NoPublishedChapters))
        {
            db.Chapters.AddRange(Seed.Chapters(novel, 3, Now.AddDays(-10)));
        }
        db.Chapters.AddRange(Seed.Chapters(NoPublishedChapters, 1, Now.AddDays(-5), status: "Draft"));
        await db.SaveChangesAsync();

        await new RankingService(db, new FixedTimeProvider(Now), NullLogger<RankingService>.Instance).CalculateAllGenreRankings();
    }

    public static readonly IMapper Mapper = new MapperConfiguration(cfg =>
    {
        cfg.AddProfile<NovelProfiles>();
        cfg.AddProfile<RankingsProfile>();
    }).CreateMapper();

    public static GetNovelsInGenreQueryHandler Handler(ApplicationDbContext db) => new(
        NullLogger<GetNovelsInGenreQueryHandler>.Instance,
        new NovelGenresRepository(db),
        new RankingRepository(db),
        new GenresRepository(db),
        Mapper);
}

public class GenreListingTests(GenreWorld world) : IClassFixture<GenreWorld>
{
    private static readonly string[] AllSortings = ["popular", "newest", "rating", "most_reviewed", "trending", "top_rated", "new"];

    private async Task<Application.Common.PagedResult<NovelInRankingDto>> List(
        string sorting, bool? isCompleted = null, int pageSize = 100, int pageNumber = 1, string? slug = null)
    {
        await using var db = world.CreateContext();
        return await GenreWorld.Handler(db).Handle(
            new GetNovelsInGenreQuery(slug ?? world.Genre.Slug, pageSize, pageNumber, sorting, isCompleted),
            CancellationToken.None);
    }

    [Theory]
    [MemberData(nameof(Sortings))]
    public async Task Without_a_status_filter_every_readable_novel_is_listed(string sorting)
    {
        var page = await List(sorting);

        Assert.Equal(world.Visible.Select(n => n.Id).Order(), page.Items.Select(n => n.Id).Order());
        Assert.Equal(5, page.TotalItemsCount);
    }

    [Theory]
    [MemberData(nameof(Sortings))]
    public async Task The_status_filter_applies_to_every_sorting(string sorting)
    {
        var completed = await List(sorting, isCompleted: true);
        var ongoing = await List(sorting, isCompleted: false);

        Assert.Equal(new[] { world.Completed.Id }, completed.Items.Select(n => n.Id));
        Assert.Equal(1, completed.TotalItemsCount);
        Assert.DoesNotContain(ongoing.Items, n => n.Id == world.Completed.Id);
        Assert.Equal(4, ongoing.TotalItemsCount);
    }

    [Theory]
    [MemberData(nameof(Sortings))]
    public async Task Every_listed_novel_carries_its_genres(string sorting)
    {
        var page = await List(sorting);

        Assert.All(page.Items, n => Assert.Contains(n.GenresList, g => g.Slug == world.Genre.Slug));
        var ongoing = page.Items.Single(n => n.Id == world.Ongoing.Id);
        Assert.Equal(
            new[] { world.Genre.Slug, world.OtherGenre.Slug }.Order(),
            ongoing.GenresList.Select(g => g.Slug).Order());
    }

    [Fact]
    public async Task A_novel_without_published_chapters_is_listed_but_not_ranked()
    {
        var listed = await List("popular", slug: world.OtherGenre.Slug);
        Assert.Equal(
            new[] { world.Ongoing.Id, world.NoPublishedChapters.Id }.Order(),
            listed.Items.Select(n => n.Id).Order());

        // Rankings need something to read, so the ranked sortings still skip it.
        var ranked = await List("trending", slug: world.OtherGenre.Slug);
        Assert.Equal(new[] { world.Ongoing.Id }, ranked.Items.Select(n => n.Id));
    }

    [Fact]
    public async Task Sortings_order_as_labelled()
    {
        Assert.Equal(world.Ongoing.Id, (await List("popular")).Items.First().Id);          // most views
        Assert.Equal(world.Completed.Id, (await List("rating")).Items.First().Id);         // 4.5 vs 0
        Assert.Equal(world.Completed.Id, (await List("most_reviewed")).Items.First().Id);  // 2 reviews vs 0
        Assert.Contains((await List("newest")).Items.First().Id, world.Ties.Select(t => t.Id));
    }

    [Theory]
    [InlineData("popular")]
    [InlineData("trending")]
    public async Task An_unknown_genre_is_a_404(string sorting)
    {
        await Assert.ThrowsAsync<NotFoundException>(() => List(sorting, slug: "no-such-genre"));
    }

    [Theory]
    [InlineData("popular")]
    [InlineData("newest")]
    [InlineData("trending")]
    public async Task Paging_one_by_one_visits_every_novel_exactly_once(string sorting)
    {
        var seen = new List<Guid>();
        for (var pageNumber = 1; pageNumber <= 5; pageNumber++)
        {
            seen.AddRange((await List(sorting, pageSize: 1, pageNumber: pageNumber)).Items.Select(n => n.Id));
        }

        Assert.Equal(world.Visible.Select(n => n.Id).Order(), seen.Order());
    }

    [Theory]
    [InlineData("popular")]
    [InlineData("trending")]
    public async Task Out_of_range_paging_is_clamped_instead_of_failing(string sorting)
    {
        var zero = await List(sorting, pageSize: 0, pageNumber: 0);
        Assert.Single(zero.Items);
        Assert.Equal(5, zero.TotalPages);

        var huge = await List(sorting, pageSize: 100_000, pageNumber: -3);
        Assert.Equal(5, huge.Items.Count());
        Assert.Equal(1, huge.TotalPages);
    }

    public static TheoryData<string> Sortings() => new(AllSortings);
}
