using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Domain.Entities;
using Domain.Seo;
using Infrastructure.Persistence;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Sareed_novels_backend.Tests.Integration;

/// <summary>
/// #68: each novel of a ranked list names its author, <c>author: { id, userName, displayName, profilePhoto }</c>, the
/// novel page's own (the shape of #59), wherever a list answers with NovelInRankingDto: the site-wide lists
/// (GET /api/rankings/site-wide/{type}: Trending, AllTime, NewArrivals, the app's home rails), a genre's rankings
/// (GET /api/rankings/{genreSlug}/{type}) and a genre's novels in every sorting (GET /api/genre/{genreSlug}/novels). A
/// ranking stores its novels' places only, so the author is read with each page, as the account is now. Its own API and
/// database, so the site-wide lists hold only these tests' novels.
/// </summary>
public class RankedNovelAuthorHttpTests(SardApiFactory api) : IClassFixture<SardApiFactory>
{
    private static readonly string[] GenreSortings = ["popular", "newest", "rating", "most_reviewed", "trending", "top_rated", "new"];

    /// <summary>An author as the lists should name them.</summary>
    private sealed record Writer(ApiUser User, string DisplayName, string? Photo);

    /// <summary>A member who writes under <paramref name="displayName"/>, with a profile photo or none.</summary>
    private async Task<Writer> NewWriter(string displayName, bool withPhoto)
    {
        var user = await api.SignUp();
        (await api.Send(HttpMethod.Patch, "/api/User/update-me", user, ReaderApi.Form(("DisplayName", displayName)))).EnsureSuccessStatusCode();
        string? photo = null;
        if (withPhoto)
        {
            photo = $"https://files.test/profile-images/{user.Id}/{Guid.NewGuid():N}.webp";
            await using var db = api.Db();
            await db.Users.Where(u => u.Id == user.Id).ExecuteUpdateAsync(s => s.SetProperty(u => u.ProfilePhoto, photo));
        }
        return new Writer(user, displayName, photo);
    }

    private async Task<Genre> NewGenre()
    {
        var genre = Seed.Genre();
        await using var db = api.Db();
        db.Genres.Add(genre);
        await db.SaveChangesAsync();
        return genre;
    }

    /// <summary>
    /// A novel by <paramref name="writer"/> in <paramref name="genre"/> that every list ranks: three chapters out
    /// yesterday (new, and enough for the quality lists) and someone reading it (for All Time).
    /// </summary>
    private async Task<Novel> RankableNovel(Writer writer, Genre genre)
    {
        var reader = await api.SignUp();
        var novel = Seed.Novel(new User { Id = writer.User.Id }, "رواية " + Seed.Marker());
        novel.Slug = Slugs.For(novel.Id, novel.Title);
        novel.NovelGenres.Add(new NovelGenre { Novel = novel, GenreId = genre.Id });
        var chapters = Seed.Chapters(novel, 3, DateTime.UtcNow.AddDays(-1));

        await using var db = api.Db();
        db.Novels.Add(novel);
        db.Chapters.AddRange(chapters);
        db.UserNovelProgress.Add(Seed.Progress(new User { Id = reader.Id }, chapters[2], 3, DateTime.UtcNow.AddHours(-1)));
        await db.SaveChangesAsync();
        return novel;
    }

    /// <summary>The rankings computed as the job computes them, through the admin endpoint.</summary>
    private async Task Rank()
    {
        var admin = await api.SignUpAdmin();
        await (await api.Send(HttpMethod.Post, "/api/admin/ranking-test/calculate-all", admin)).OkJson();
    }

    /// <summary>The site-wide lists (the home's rails), at most <paramref name="pageSize"/> novels.</summary>
    private static IEnumerable<string> SiteWideLists(int pageSize = 100) =>
        new[] { "Trending", "AllTime", "NewArrivals" }.Select(type => $"/api/rankings/site-wide/{type}?pageSize={pageSize}");

    /// <summary>A genre's lists: its three rankings, and its novels in every sorting (the ranked ones by status too).</summary>
    private static IEnumerable<string> GenreLists(Genre genre) =>
        new[] { "trending", "top_rated", "new" }.Select(type => $"/api/rankings/{genre.Slug}/{type}?pageSize=100")
            .Concat(GenreSortings.Select(sorting => $"/api/genre/{genre.Slug}/novels?sorting={sorting}&pageSize=100"))
            .Concat(new[] { "trending", "top_rated", "new" }.Select(sorting =>
                $"/api/genre/{genre.Slug}/novels?sorting={sorting}&isCompleted=false&pageSize=100"));

    private async Task<List<JsonElement>> Items(string list) =>
        (await (await api.Get(list)).OkJson()).GetProperty("items").EnumerateArray().ToList();

    /// <summary>The novel page's author (GET /api/novel/by-id/{id}).</summary>
    private async Task<JsonElement> NovelPageAuthor(Novel novel) =>
        (await (await api.Get($"/api/novel/by-id/{novel.Id}")).OkJson()).GetProperty("author");

    /// <summary>
    /// <paramref name="novel"/> is in <paramref name="items"/> of <paramref name="list"/>, naming <paramref name="writer"/>
    /// as the novel page does: the same JSON, with the account's id, its current names and its photo (null without one).
    /// </summary>
    private async Task AssertAuthor(string list, List<JsonElement> items, Novel novel, Writer writer)
    {
        var listed = items.Where(i => i.GetProperty("id").GetGuid() == novel.Id).ToList();
        Assert.True(listed.Count == 1, $"{list} lists {novel.Id} {listed.Count} times");
        Assert.True(listed[0].TryGetProperty("author", out var author), $"{list} names no author");
        Assert.Equal(["id", "userName", "displayName", "profilePhoto"], author.EnumerateObject().Select(p => p.Name));
        Assert.Equal(writer.User.Id, author.GetProperty("id").GetString());
        Assert.Equal(writer.User.UserName, author.GetProperty("userName").GetString());
        Assert.Equal(writer.DisplayName, author.GetProperty("displayName").GetString());
        Assert.Equal(writer.Photo, author.GetProperty("profilePhoto").GetString());
        Assert.Equal((await NovelPageAuthor(novel)).GetRawText(), author.GetRawText());
    }

    private async Task AssertAuthors(Genre genre, params (Novel Novel, Writer Writer)[] novels)
    {
        foreach (var list in SiteWideLists().Concat(GenreLists(genre)))
        {
            var items = await Items(list);
            foreach (var (novel, writer) in novels)
            {
                await AssertAuthor(list, items, novel, writer);
            }
        }
    }

    [Fact]
    public async Task Every_ranked_list_names_each_novels_author_as_the_novel_page_does()
    {
        var sara = await NewWriter("سارة الكاتبة", withPhoto: true);
        var reem = await NewWriter("ريم", withPhoto: false);
        var genre = await NewGenre();
        var first = await RankableNovel(sara, genre);
        var second = await RankableNovel(reem, genre);
        var third = await RankableNovel(sara, genre);
        await Rank();

        await AssertAuthors(genre, (first, sara), (second, reem), (third, sara));
    }

    [Fact]
    public async Task After_a_rename_every_ranked_list_shows_the_new_names_in_the_next_answer_without_ranking_again()
    {
        var writer = await NewWriter("الاسم القديم", withPhoto: false);
        var genre = await NewGenre();
        var novel = await RankableNovel(writer, genre);
        await Rank();
        await AssertAuthors(genre, (novel, writer));
        DateTime ranked;
        await using (var db = api.Db())
        {
            ranked = await db.RankingLists.MaxAsync(l => l.LastUpdated);
        }

        // A new user name, display name and photo, all at once.
        var newUserName = "r" + Guid.NewGuid().ToString("N")[..10];
        using var form = ReaderApi.Form(("UserName", newUserName), ("DisplayName", "الاسم الجديد"));
        var image = new ByteArrayContent([0x89, 0x50, 0x4E, 0x47]);
        image.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        form.Add(image, "ProfilePhoto", "photo.png");
        (await api.Send(HttpMethod.Patch, "/api/User/update-me", writer.User, form)).EnsureSuccessStatusCode();
        var photo = (await NovelPageAuthor(novel)).GetProperty("profilePhoto").GetString();
        Assert.StartsWith($"https://files.test/profile-images/{writer.User.Id}/", photo);

        // The rankings weren't computed again (the job runs every 30 minutes): every list names the author anew already.
        var renamed = new Writer(writer.User with { UserName = newUserName }, "الاسم الجديد", photo);
        await AssertAuthors(genre, (novel, renamed));
        await using (var db = api.Db())
        {
            Assert.Equal(ranked, await db.RankingLists.MaxAsync(l => l.LastUpdated));
        }
    }

    [Fact]
    public async Task A_deleted_authors_novels_leave_every_ranked_list_at_once()
    {
        var leaving = await NewWriter("كاتب يحذف حسابه", withPhoto: true);
        var staying = await NewWriter("كاتبة باقية", withPhoto: false);
        var genre = await NewGenre();
        var gone = await RankableNovel(leaving, genre);
        var kept = await RankableNovel(staying, genre);
        await Rank();
        await AssertAuthors(genre, (gone, leaving), (kept, staying));

        Assert.Equal(HttpStatusCode.NoContent, (await api.Send(HttpMethod.Delete, "/api/User/me", leaving.User,
            JsonContent.Create(new { password = ModerationApi.Password }))).StatusCode);

        // Its novels were deleted with the account (AccountDeletionService); the rankings, not computed again, read
        // their novels with each page, so no list shows it, while the others keep their authors.
        foreach (var list in SiteWideLists().Concat(GenreLists(genre)))
        {
            var items = await Items(list);
            Assert.DoesNotContain(items, i => i.GetProperty("id").GetGuid() == gone.Id);
            await AssertAuthor(list, items, kept, staying);
        }
    }

    [Fact]
    public async Task A_page_of_one_novel_or_of_many_by_different_authors_is_the_same_sql_commands()
    {
        var one = await NewGenre();
        await RankableNovel(await NewWriter("كاتبة واحدة", withPhoto: true), one);
        var many = await NewGenre();
        for (var i = 0; i < 6; i++)
        {
            await RankableNovel(await NewWriter($"كاتب {i}", withPhoto: i % 2 == 0), many);
        }
        await Rank();

        var log = new CommandsByRequest();
        await using var counted = api.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
            services.ConfigureDbContext<ApplicationDbContext>(options => options.AddInterceptors(log))));
        using var client = counted.CreateClient();

        async Task<(List<JsonElement> Items, List<string> Commands)> Measured(string list)
        {
            var marker = Guid.NewGuid().ToString("N");
            var request = new HttpRequestMessage(HttpMethod.Get, list);
            request.Headers.Add(CommandsByRequest.Header, marker);
            var page = await (await client.SendAsync(request)).OkJson();
            return (page.GetProperty("items").EnumerateArray().ToList(), log.Of(marker));
        }

        // The same list for one novel and for several, each by a different author: a genre's lists for the two genres,
        // the site-wide ones a page of one and a page of every novel.
        var pairs = GenreLists(one).Zip(GenreLists(many))
            .Concat(SiteWideLists(pageSize: 1).Zip(SiteWideLists(pageSize: 100)));
        foreach (var (single, several) in pairs)
        {
            var (oneItem, oneCommands) = await Measured(single);
            var (items, commands) = await Measured(several);

            Assert.Single(oneItem);
            Assert.NotNull(oneItem[0].GetProperty("author").GetProperty("userName").GetString());
            var authors = items.Select(i => i.GetProperty("author").GetProperty("id").GetString()).Distinct().Count();
            Assert.True(authors >= 6, $"{several}: {items.Count} novels by {authors} authors");
            // The authors come in the page's own query: no query per novel, nor one for the authors.
            Assert.True(oneCommands.Count == commands.Count, $"{several}: {oneCommands.Count} then {commands.Count} commands");
            Assert.Single(commands, sql => sql.Contains("[AspNetUsers]"));
        }
    }
}
