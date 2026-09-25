using Application.Search.DTOs;
using Domain.Entities;
using Infrastructure.Services.Search;

namespace Sareed_novels_backend.Tests.Integration;

public class SearchServiceTests(SqlServerDatabase database) : IClassFixture<SqlServerDatabase>
{
    private static readonly DateTime LongAgo = DateTime.UtcNow.AddDays(-100);

    private async Task<(User Author, Genre Genre)> SeedAuthorAndGenre()
    {
        await using var db = database.CreateContext();
        var author = Seed.User();
        var genre = Seed.Genre();
        db.Users.Add(author);
        db.Genres.Add(genre);
        await db.SaveChangesAsync();
        return (author, genre);
    }

    private async Task<Novel> AddNovel(User author, string title, bool isDraft = false, int chapters = 1, int draftChapters = 0, Genre? genre = null)
    {
        await using var db = database.CreateContext();
        var novel = Seed.Novel(author, title, isDraft);
        db.Novels.Add(novel);
        db.Chapters.AddRange(Seed.Chapters(novel, chapters, LongAgo));
        db.Chapters.AddRange(Seed.Chapters(novel, draftChapters, LongAgo, status: "Draft", startIndex: chapters + 1));
        if (genre != null)
        {
            db.NovelGenres.Add(new NovelGenre { NovelId = novel.Id, GenreId = genre.Id });
        }
        await db.SaveChangesAsync();
        return novel;
    }

    [Fact]
    public async Task Finds_titles_across_arabic_spelling_variants_and_ranks_the_best_match_first()
    {
        var m = Seed.Marker();
        var (author, _) = await SeedAuthorAndGenre();
        var contains = await AddNovel(author, $"أسرار {m} المدرسة");
        var prefix = await AddNovel(author, $"{m} الْمَدْرَسَةُ المسحورة");
        var exact = await AddNovel(author, $"{m} المدرسة");
        await AddNovel(author, $"{m} المدرسة السرية", isDraft: true);
        await AddNovel(author, $"{m} مكتبة");

        await using var db = database.CreateContext();
        // Typed with ه instead of ة and without tashkeel.
        var result = await new NovelSearchService(db).SearchNovelsAsync(new SearchNovelsRequest { Query = $"{m} المدرسه" });

        Assert.Equal(3, result.TotalItemsCount);
        Assert.Equal(new[] { exact.Id, prefix.Id, contains.Id }, result.Items.Select(i => i.Id).ToArray());
    }

    [Fact]
    public async Task Words_can_come_in_any_order()
    {
        var m = Seed.Marker();
        var (author, _) = await SeedAuthorAndGenre();
        var novel = await AddNovel(author, $"شيخ في محراب قلبي {m}");

        await using var db = database.CreateContext();
        var result = await new NovelSearchService(db).SearchNovelsAsync(new SearchNovelsRequest { Query = $"{m} قلبي شيخ" });

        Assert.Equal(novel.Id, Assert.Single(result.Items).Id);
    }

    [Fact]
    public async Task Filters_by_genre_name_and_counts_only_published_chapters()
    {
        var m = Seed.Marker();
        var (author, genre) = await SeedAuthorAndGenre();
        var inGenre = await AddNovel(author, $"{m} alpha", chapters: 12, draftChapters: 3, genre: genre);
        await AddNovel(author, $"{m} beta", chapters: 12);

        await using var db = database.CreateContext();
        var service = new NovelSearchService(db);

        var byGenre = await service.SearchNovelsAsync(new SearchNovelsRequest { Query = m, Genres = [genre.Name] });
        var hit = Assert.Single(byGenre.Items);
        Assert.Equal(inGenre.Id, hit.Id);
        Assert.Equal(12, hit.ChapterCount);
        Assert.Contains(genre.Name, hit.Genres);

        var tenToTwenty = await service.SearchNovelsAsync(new SearchNovelsRequest
            { Query = m, Genres = [genre.Name], ChapterRanges = [ChapterCountRange.Range_10_20] });
        Assert.Single(tenToTwenty.Items);

        var upToTen = await service.SearchNovelsAsync(new SearchNovelsRequest
            { Query = m, Genres = [genre.Name], ChapterRange = ChapterCountRange.Range_1_10 });
        Assert.Empty(upToTen.Items);
    }

    [Fact]
    public async Task Pages_through_results_and_caps_page_size()
    {
        var m = Seed.Marker();
        var (author, _) = await SeedAuthorAndGenre();
        for (var i = 0; i < 5; i++)
        {
            await AddNovel(author, $"{m} رواية {i}");
        }

        await using var db = database.CreateContext();
        var service = new NovelSearchService(db);

        var page2 = await service.SearchNovelsAsync(new SearchNovelsRequest { Query = m, PageSize = 2, PageNumber = 2 });
        Assert.Equal(5, page2.TotalItemsCount);
        Assert.Equal(2, page2.Items.Count());

        var huge = await service.SearchNovelsAsync(new SearchNovelsRequest { Query = m, PageSize = 10_000 });
        Assert.Equal(5, huge.Items.Count());
    }

    [Fact]
    public async Task Finds_users_by_arabic_display_name_or_user_name()
    {
        var m = Seed.Marker();
        var user = Seed.User(displayName: $"أحمد الكاتب {m}", userName: $"ahmed_{m}");
        await using (var seed = database.CreateContext())
        {
            seed.Users.Add(user);
            await seed.SaveChangesAsync();
        }

        await using var db = database.CreateContext();
        var service = new UserSearchService(db);

        var byDisplayName = await service.SearchUsersAsync(new SearchUsersRequest { Query = $"احمد {m}" });
        Assert.Equal(user.Id, Assert.Single(byDisplayName.Items).Id);

        var byUserName = await service.SearchUsersAsync(new SearchUsersRequest { Query = $"ahmed_{m}" });
        Assert.Equal(user.Id, byUserName.Items.First().Id);
    }

    [Fact]
    public async Task Search_columns_follow_title_changes()
    {
        var m = Seed.Marker();
        var (author, _) = await SeedAuthorAndGenre();
        var novel = await AddNovel(author, $"{m} قديم");

        await using (var db = database.CreateContext())
        {
            var tracked = await db.Novels.FindAsync(novel.Id);
            tracked!.Title = $"{m} جديد";
            await db.SaveChangesAsync();
        }

        await using var check = database.CreateContext();
        var service = new NovelSearchService(check);
        Assert.Empty((await service.SearchNovelsAsync(new SearchNovelsRequest { Query = $"{m} قديم" })).Items);
        Assert.Single((await service.SearchNovelsAsync(new SearchNovelsRequest { Query = $"{m} جديد" })).Items);
    }
}
