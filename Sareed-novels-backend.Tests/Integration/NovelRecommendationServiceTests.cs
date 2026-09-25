using Domain.Entities;
using Domain.Exceptions;
using Infrastructure.Services.Search;
using Microsoft.Extensions.Caching.Memory;

namespace Sareed_novels_backend.Tests.Integration;

/// <summary>
/// Every test here shares one database, so each one uses its own genres and authors and only asserts about its own
/// novels; other tests' novels share no genre with the source and score near zero.
/// </summary>
public class NovelRecommendationServiceTests(SqlServerDatabase database) : IClassFixture<SqlServerDatabase>
{
    private static readonly DateTime LongAgo = DateTime.UtcNow.AddDays(-100);

    private NovelRecommendationService CreateService(Infrastructure.Persistence.ApplicationDbContext db) =>
        new(db, new MemoryCache(new MemoryCacheOptions { SizeLimit = 1000 }));

    private async Task<User[]> AddUsers(int count)
    {
        await using var db = database.CreateContext();
        var users = Enumerable.Range(0, count).Select(_ => Seed.User()).ToArray();
        db.Users.AddRange(users);
        await db.SaveChangesAsync();
        return users;
    }

    private async Task<Genre[]> AddGenres(int count)
    {
        await using var db = database.CreateContext();
        var genres = Enumerable.Range(0, count).Select(_ => Seed.Genre()).ToArray();
        db.Genres.AddRange(genres);
        await db.SaveChangesAsync();
        return genres;
    }

    private async Task<(Novel Novel, Chapter FirstChapter)> AddNovel(
        User author, Genre[] genres, bool isDraft = false, bool isDeleted = false, bool eligible = true,
        int chapters = 1, int draftChapters = 0)
    {
        await using var db = database.CreateContext();
        var novel = Seed.Novel(author, Seed.Marker(), isDraft);
        novel.IsDeleted = isDeleted;
        novel.IsEligibleForRanking = eligible;
        db.Novels.Add(novel);
        var published = Seed.Chapters(novel, chapters, LongAgo);
        db.Chapters.AddRange(published);
        db.Chapters.AddRange(Seed.Chapters(novel, draftChapters, LongAgo, status: "Draft", startIndex: chapters + 1));
        db.NovelGenres.AddRange(genres.Select(g => new NovelGenre { NovelId = novel.Id, GenreId = g.Id }));
        await db.SaveChangesAsync();
        return (novel, published.FirstOrDefault()!);
    }

    private async Task Read(User reader, params (Novel Novel, Chapter FirstChapter)[] novels)
    {
        await using var db = database.CreateContext();
        db.UserNovelProgress.AddRange(novels.Select(n => Seed.Progress(reader, n.FirstChapter, 1, LongAgo)));
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task Recommends_only_other_readable_published_novels()
    {
        var genres = await AddGenres(1);
        var authors = await AddUsers(2);
        var source = await AddNovel(authors[0], genres);
        var good = await AddNovel(authors[1], genres);
        var draft = await AddNovel(authors[1], genres, isDraft: true);
        var onlyDraftChapters = await AddNovel(authors[1], genres, chapters: 0, draftChapters: 2);
        var deleted = await AddNovel(authors[1], genres, isDeleted: true);
        var excludedFromRanking = await AddNovel(authors[1], genres, eligible: false);

        await using var db = database.CreateContext();
        var result = await CreateService(db).GetRecommendationsAsync(source.Novel.Id, 20);
        var ids = result.Select(r => r.Id).ToList();

        Assert.Equal(good.Novel.Id, ids.First());
        Assert.DoesNotContain(source.Novel.Id, ids);
        Assert.DoesNotContain(draft.Novel.Id, ids);
        Assert.DoesNotContain(onlyDraftChapters.Novel.Id, ids);
        Assert.DoesNotContain(deleted.Novel.Id, ids);
        Assert.DoesNotContain(excludedFromRanking.Novel.Id, ids);
        Assert.All(result, r => Assert.True(r.ChapterCount > 0));
    }

    [Fact]
    public async Task Readers_who_read_both_novels_raise_a_recommendation_but_authors_reading_their_own_novel_do_not()
    {
        var genres = await AddGenres(1);
        var users = await AddUsers(6);
        var (sourceAuthor, selfPromotingAuthor, otherAuthor, thirdAuthor, reader) =
            (users[0], users[1], users[2], users[3], users[4]);

        var source = await AddNovel(sourceAuthor, genres);
        var selfPromoted = await AddNovel(selfPromotingAuthor, genres);
        var coRead = await AddNovel(otherAuthor, genres);
        var readBySourceAuthor = await AddNovel(thirdAuthor, genres);

        // A real reader read the source and coRead.
        await Read(reader, source, coRead);
        // An author read the source and previewed their own novel: not a co-reader of their own novel.
        await Read(selfPromotingAuthor, source, selfPromoted);
        // The source's author read their own novel and someone else's: not a reader of the source.
        await Read(sourceAuthor, source, readBySourceAuthor);

        await using var db = database.CreateContext();
        var result = await CreateService(db).GetRecommendationsAsync(source.Novel.Id, 20);

        var coReadHit = result.Single(r => r.Id == coRead.Novel.Id);
        var selfPromotedHit = result.Single(r => r.Id == selfPromoted.Novel.Id);
        var sourceAuthorHit = result.Single(r => r.Id == readBySourceAuthor.Novel.Id);

        Assert.Equal(coRead.Novel.Id, result[0].Id);
        // Genre only: 1 shared genre, no readers (the author's own progress is not a reader).
        Assert.Equal(1.0m, selfPromotedHit.SimilarityScore);
        // Genre plus the popularity tie-break for its one (non-author) reader, and no co-readership.
        Assert.Equal((decimal)Math.Round(1 + 0.1 * Math.Log(2), 4), sourceAuthorHit.SimilarityScore);
        Assert.True(coReadHit.SimilarityScore > sourceAuthorHit.SimilarityScore + 1);
    }

    [Fact]
    public async Task One_author_takes_at_most_two_places_before_other_authors()
    {
        var genres = await AddGenres(2);
        var users = await AddUsers(4);
        var (sourceAuthor, prolific, second, third) = (users[0], users[1], users[2], users[3]);

        var source = await AddNovel(sourceAuthor, genres);
        var prolificNovels = new List<Guid>();
        for (var i = 0; i < 4; i++)
        {
            prolificNovels.Add((await AddNovel(prolific, genres)).Novel.Id); // 2 shared genres each
        }
        var fromSecond = await AddNovel(second, [genres[0]]); // 1 shared genre
        var fromThird = await AddNovel(third, [genres[1]]); // 1 shared genre

        await using var db = database.CreateContext();
        var ids = (await CreateService(db).GetRecommendationsAsync(source.Novel.Id, 20)).Select(r => r.Id).ToList();

        Assert.Equal(2, ids.Take(2).Count(prolificNovels.Contains));
        Assert.Equal(new[] { fromSecond.Novel.Id, fromThird.Novel.Id }.OrderBy(id => id), ids.Skip(2).Take(2).OrderBy(id => id));
        // The prolific author's other novels are pushed down, not dropped.
        Assert.All(ids.Where(prolificNovels.Contains).Skip(2),
            id => Assert.True(ids.IndexOf(id) > ids.IndexOf(fromThird.Novel.Id)));
    }

    [Fact]
    public async Task Unknown_or_deleted_novels_are_not_found_instead_of_an_empty_list()
    {
        var genres = await AddGenres(1);
        var authors = await AddUsers(1);
        var deleted = await AddNovel(authors[0], genres, isDeleted: true);

        await using var db = database.CreateContext();
        var service = CreateService(db);

        await Assert.ThrowsAsync<NotFoundException>(() => service.GetRecommendationsAsync(Guid.NewGuid()));
        await Assert.ThrowsAsync<NotFoundException>(() => service.GetRecommendationsAsync(deleted.Novel.Id));
        // Not cached as an empty success either.
        await Assert.ThrowsAsync<NotFoundException>(() => service.GetRecommendationsAsync(deleted.Novel.Id));
    }

    [Fact]
    public async Task A_draft_novel_still_gets_recommendations_for_its_author_preview()
    {
        var genres = await AddGenres(1);
        var authors = await AddUsers(2);
        var draftSource = await AddNovel(authors[0], genres, isDraft: true);
        var other = await AddNovel(authors[1], genres);

        await using var db = database.CreateContext();
        var result = await CreateService(db).GetRecommendationsAsync(draftSource.Novel.Id, 5);

        Assert.Equal(other.Novel.Id, result[0].Id);
        Assert.InRange(result.Count, 1, 5);
    }
}
