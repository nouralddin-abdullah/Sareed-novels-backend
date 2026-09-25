using Application.Search.DTOs;
using Application.Search.Queries.SuggestNovels;
using Domain.Entities;
using Infrastructure.Services.Search;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

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

    [Fact]
    public async Task Drafts_are_hidden_but_novels_without_published_chapters_are_listed()
    {
        var m = Seed.Marker();
        var (author, genre) = await SeedAuthorAndGenre();
        var published = await AddNovel(author, $"{m} منشورة", genre: genre);
        var empty = await AddNovel(author, $"{m} فارغة", chapters: 0, genre: genre);
        var draftChaptersOnly = await AddNovel(author, $"{m} مسودات", chapters: 0, draftChapters: 2, genre: genre);
        await AddNovel(author, $"{m} مخفية", isDraft: true, genre: genre);
        var expected = new[] { published.Id, empty.Id, draftChaptersOnly.Id }.Order();

        await using var db = database.CreateContext();
        var service = new NovelSearchService(db);
        var byQuery = await service.SearchNovelsAsync(new SearchNovelsRequest { Query = m });
        Assert.Equal(expected, byQuery.Items.Select(n => n.Id).Order());
        Assert.Equal(3, byQuery.TotalItemsCount);

        // Browsing without a query (the search page before anything is typed) follows the same rule.
        var browse = await service.SearchNovelsAsync(new SearchNovelsRequest { Genres = [genre.Name] });
        Assert.Equal(expected, browse.Items.Select(n => n.Id).Order());
    }

    [Theory]
    [InlineData("%")]
    [InlineData("_")]
    [InlineData("[")]
    [InlineData("%%")]
    [InlineData("!!")]
    [InlineData("💫")]
    [InlineData("🇵🇸")]
    public async Task A_query_with_nothing_searchable_finds_nothing_instead_of_everything(string query)
    {
        var (author, _) = await SeedAuthorAndGenre();
        var novel = await AddNovel(author, $"{Seed.Marker()} خطوة 💫");
        await AddEntities(novel, ("شخصية", "راع", null));

        await using var db = database.CreateContext();
        var novels = await new NovelSearchService(db).SearchNovelsAsync(new SearchNovelsRequest { Query = query });
        var users = await new UserSearchService(db).SearchUsersAsync(new SearchUsersRequest { Query = query });
        var entities = await new EntitySearchService(db).SearchEntitiesAsync(novel.Id, query);
        var suggestions = await new SuggestNovelsQueryHandler(
                NullLogger<SuggestNovelsQueryHandler>.Instance, new NovelSearchService(db))
            .Handle(new SuggestNovelsQuery(query), CancellationToken.None);

        Assert.Equal(0, novels.TotalItemsCount);
        Assert.Equal(0, users.TotalItemsCount);
        Assert.Equal(0, entities.TotalItemsCount);
        Assert.Empty(suggestions);
    }

    [Fact]
    public async Task Sql_wildcards_and_emoji_in_a_query_are_plain_separators()
    {
        var m = Seed.Marker();
        var (author, _) = await SeedAuthorAndGenre();
        var percent = await AddNovel(author, $"{m} 100% حقيقة");
        var emoji = await AddNovel(author, $"{m} خطوة 💫");
        await AddNovel(author, $"{m} ac");

        await using var db = database.CreateContext();
        var service = new NovelSearchService(db);

        Assert.Equal(percent.Id, Assert.Single((await service.SearchNovelsAsync(new SearchNovelsRequest { Query = $"[{m}] 100%" })).Items).Id);
        Assert.Equal(emoji.Id, Assert.Single((await service.SearchNovelsAsync(new SearchNovelsRequest { Query = $"{m} خطوة 💫" })).Items).Id);
        // As a LIKE pattern "[ab]c" would match "ac"; here brackets only split words, so it asks for "ab" and "c".
        Assert.Empty((await service.SearchNovelsAsync(new SearchNovelsRequest { Query = $"{m} [ab]c" })).Items);
    }

    [Fact]
    public async Task Whitespace_only_query_browses_every_listed_novel()
    {
        var (author, _) = await SeedAuthorAndGenre();
        await AddNovel(author, $"{Seed.Marker()} تصفح");

        await using var db = database.CreateContext();
        var result = await new NovelSearchService(db).SearchNovelsAsync(new SearchNovelsRequest { Query = "   " });

        Assert.True(result.TotalItemsCount >= 1);
    }

    [Fact]
    public async Task A_huge_page_number_returns_an_empty_page_instead_of_failing()
    {
        var m = Seed.Marker();
        var (author, _) = await SeedAuthorAndGenre();
        var novel = await AddNovel(author, $"{m} صفحة");
        var user = Seed.User(displayName: $"{m} كاتب");
        await using (var seed = database.CreateContext())
        {
            seed.Users.Add(user);
            await seed.SaveChangesAsync();
        }

        await using var db = database.CreateContext();
        var novels = await new NovelSearchService(db).SearchNovelsAsync(new SearchNovelsRequest { Query = m, PageNumber = int.MaxValue });
        var users = await new UserSearchService(db).SearchUsersAsync(new SearchUsersRequest { Query = m, PageNumber = int.MaxValue });
        var entities = await new EntitySearchService(db).SearchEntitiesAsync(novel.Id, m, pageNumber: int.MaxValue);

        Assert.Empty(novels.Items);
        Assert.Equal(1, novels.TotalItemsCount);
        Assert.Empty(users.Items);
        Assert.Equal(1, users.TotalItemsCount);
        Assert.Empty(entities.Items);
    }

    [Fact]
    public async Task A_renamed_user_is_found_by_the_new_name_right_away()
    {
        var m = Seed.Marker();
        var user = Seed.User(displayName: $"{m} قديم");
        await using (var seed = database.CreateContext())
        {
            seed.Users.Add(user);
            await seed.SaveChangesAsync();
        }

        await using (var db = database.CreateContext())
        {
            var tracked = await db.Users.SingleAsync(u => u.Id == user.Id);
            tracked.DisplayName = $"{m} سارة";
            await db.SaveChangesAsync();
        }

        await using var check = database.CreateContext();
        var service = new UserSearchService(check);
        Assert.Empty((await service.SearchUsersAsync(new SearchUsersRequest { Query = $"{m} قديم" })).Items);
        Assert.Equal(user.Id, Assert.Single((await service.SearchUsersAsync(new SearchUsersRequest { Query = $"{m} ساره" })).Items).Id);
    }

    [Fact]
    public async Task Wiki_search_skips_empty_section_placeholders_and_matches_descriptions()
    {
        var (author, _) = await SeedAuthorAndGenre();
        var novel = await AddNovel(author, $"{Seed.Marker()} ويكي");
        await AddEntities(novel,
            ("شخصيات", "_section_شخصيات", null),
            ("شخصيات", "الجوكر", "بطاقة الجوكر الأخيرة"),
            ("أماكن", "القلعة", "حيث تبدأ الحكاية"));

        await using var db = database.CreateContext();
        var service = new EntitySearchService(db);

        var all = await service.SearchEntitiesAsync(novel.Id);
        Assert.Equal(new[] { "الجوكر", "القلعة" }, all.Items.Select(e => e.Name).OrderBy(n => n).ToArray());
        Assert.Empty((await service.SearchEntitiesAsync(novel.Id, "شخصيات")).Items);
        Assert.Empty((await service.SearchEntitiesAsync(novel.Id, "section")).Items);

        var byDescription = await service.SearchEntitiesAsync(novel.Id, "الحكايه");
        Assert.Equal("القلعة", Assert.Single(byDescription.Items).Name);
        var byName = await service.SearchEntitiesAsync(novel.Id, "جوك", section: "شخصيات");
        Assert.Equal("الجوكر", Assert.Single(byName.Items).Name);
    }

    private async Task AddEntities(Novel novel, params (string Section, string Name, string? ShortDescription)[] entities)
    {
        await using var db = database.CreateContext();
        db.NovelEntities.AddRange(entities.Select(e => new NovelEntity
        {
            Id = Guid.NewGuid(),
            NovelId = novel.Id,
            Section = e.Section,
            Name = e.Name,
            ShortDescription = e.ShortDescription
        }));
        await db.SaveChangesAsync();
    }
}
