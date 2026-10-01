using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Domain.Constants;
using Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Sareed_novels_backend.Tests.Integration;

/// <summary>
/// The author of each novel in search results (GET and POST /api/search/novels) and on a reading list
/// (GET /api/readinglist/{id}), #59: <c>author: { id, userName, displayName, profilePhoto }</c>, the novel page's own
/// <c>author</c>, read from the account as it is now. A deleted account's novels are deleted with it, so neither list
/// shows one.
/// </summary>
[Collection(ReaderApiCollection.Name)]
public class NovelAuthorInListsHttpTests(SardApiFactory api)
{
    /// <summary>An author as the lists should name them.</summary>
    private sealed record Writer(ApiUser User, string DisplayName, string? Photo);

    /// <summary>A member who writes under <paramref name="displayName"/>, with this profile photo or none.</summary>
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

    /// <summary>The items of a search for <paramref name="marker"/>, asked with GET or with POST.</summary>
    private async Task<List<JsonElement>> Search(string method, string marker)
    {
        var response = method == "GET"
            ? await api.Get($"/api/search/novels?query={Uri.EscapeDataString(marker)}&pageSize=50")
            : await api.Send(HttpMethod.Post, "/api/search/novels", content: JsonContent.Create(new { query = marker, pageSize = 50 }));
        return (await response.OkJson()).GetProperty("items").EnumerateArray().ToList();
    }

    /// <summary>The list's page as <paramref name="viewer"/> opens it (anonymously when null).</summary>
    private async Task<JsonElement> ListPage(Guid list, ApiUser? viewer = null) =>
        await (await api.Get($"/api/readinglist/{list}", viewer)).OkJson();

    private async Task AddToList(ApiUser owner, Guid list, Guid novel) =>
        (await api.Send(HttpMethod.Post, $"/api/readinglist/{list}/novels/{novel}", owner)).EnsureSuccessStatusCode();

    /// <summary>The novel page's author, the shape both lists give (GET /api/novel/by-id/{id}).</summary>
    private async Task<JsonElement> NovelPageAuthor(Novel novel) =>
        (await (await api.Get($"/api/novel/by-id/{novel.Id}")).OkJson()).GetProperty("author");

    /// <summary>
    /// <paramref name="author"/> is <paramref name="writer"/> as the novel page names them: the same JSON, field for
    /// field, with the account's id, its current user name and display name, and its photo (null without one).
    /// </summary>
    private async Task AssertAuthor(Writer writer, Novel novel, JsonElement author)
    {
        Assert.Equal(writer.User.Id, author.GetProperty("id").GetString());
        Assert.Equal(writer.User.UserName, author.GetProperty("userName").GetString());
        Assert.Equal(writer.DisplayName, author.GetProperty("displayName").GetString());
        Assert.Equal(writer.Photo, author.GetProperty("profilePhoto").GetString());
        Assert.Equal((await NovelPageAuthor(novel)).GetRawText(), author.GetRawText());
    }

    private static JsonElement Item(IEnumerable<JsonElement> items, Novel novel, string id = "id") =>
        Assert.Single(items, i => i.GetProperty(id).GetGuid() == novel.Id);

    [Theory]
    [InlineData("GET")]
    [InlineData("POST")]
    public async Task Search_results_name_each_novels_author_as_the_novel_page_does(string method)
    {
        var sara = await NewWriter("سارة الكاتبة", withPhoto: true);
        var reem = await NewWriter("ريم", withPhoto: false);
        var marker = Seed.Marker();
        var first = await api.AddNovel(sara.User, title: $"رواية {marker} أولى");
        var second = await api.AddNovel(reem.User, title: $"رواية {marker} ثانية");
        var third = await api.AddNovel(sara.User, title: $"رواية {marker} ثالثة");
        await api.AddChapter(third, "<p>فقرة</p>");

        var items = await Search(method, marker);

        Assert.Equal(3, items.Count);
        await AssertAuthor(sara, first, Item(items, first).GetProperty("author"));
        await AssertAuthor(reem, second, Item(items, second).GetProperty("author"));
        await AssertAuthor(sara, third, Item(items, third).GetProperty("author"));
    }

    [Fact]
    public async Task A_reading_lists_novels_name_their_authors_as_the_novel_page_does()
    {
        var sara = await NewWriter("سارة الكاتبة", withPhoto: true);
        var reem = await NewWriter("ريم", withPhoto: false);
        var owner = await api.SignUp();
        var novels = new (Novel Novel, Writer Writer)[]
        {
            (await api.AddNovel(reem.User), reem), (await api.AddNovel(sara.User), sara), (await api.AddNovel(reem.User), reem)
        };
        var list = await api.ReadingList(owner);
        foreach (var (novel, _) in novels)
        {
            await AddToList(owner, list, novel.Id);
        }

        // The same for the owner and for anyone else.
        foreach (var viewer in new[] { owner, null })
        {
            var listed = (await ListPage(list, viewer)).GetProperty("novels").EnumerateArray().ToList();
            Assert.Equal(novels.Select(n => n.Novel.Id), listed.Select(n => n.GetProperty("novelId").GetGuid()));
            foreach (var (novel, writer) in novels)
            {
                await AssertAuthor(writer, novel, Item(listed, novel, "novelId").GetProperty("author"));
            }
        }
    }

    [Fact]
    public async Task After_a_rename_search_and_lists_show_the_new_names_at_once()
    {
        var writer = await NewWriter("الاسم القديم", withPhoto: false);
        var marker = Seed.Marker();
        var novel = await api.AddNovel(writer.User, title: $"رواية {marker}");
        var owner = await api.SignUp();
        var list = await api.ReadingList(owner);
        await AddToList(owner, list, novel.Id);
        Assert.Equal("الاسم القديم", Item(await Search("GET", marker), novel).GetProperty("author").GetProperty("displayName").GetString());

        var newUserName = "r" + Guid.NewGuid().ToString("N")[..10];
        (await api.Send(HttpMethod.Patch, "/api/User/update-me", writer.User,
            ReaderApi.Form(("UserName", newUserName), ("DisplayName", "الاسم الجديد")))).EnsureSuccessStatusCode();

        var renamed = writer with { User = writer.User with { UserName = newUserName }, DisplayName = "الاسم الجديد" };
        await AssertAuthor(renamed, novel, Item(await Search("GET", marker), novel).GetProperty("author"));
        await AssertAuthor(renamed, novel, Item(await Search("POST", marker), novel).GetProperty("author"));
        await AssertAuthor(renamed, novel, Item((await ListPage(list)).GetProperty("novels").EnumerateArray(), novel, "novelId").GetProperty("author"));
    }

    [Fact]
    public async Task A_deleted_authors_novels_are_in_neither_list()
    {
        var writer = await NewWriter("كاتب يحذف حسابه", withPhoto: true);
        var marker = Seed.Marker();
        var novel = await api.AddNovel(writer.User, title: $"رواية {marker}");
        await api.AddChapter(novel, "<p>فقرة</p>");
        var owner = await api.SignUp();
        var list = await api.ReadingList(owner);
        await AddToList(owner, list, novel.Id);

        var deletion = await api.Send(HttpMethod.Delete, "/api/User/me", writer.User, JsonContent.Create(new { password = ModerationApi.Password }));
        Assert.Equal(HttpStatusCode.NoContent, deletion.StatusCode);

        // Its novels were deleted with the account (AccountDeletionService): left out, and out of the counts.
        foreach (var method in new[] { "GET", "POST" })
        {
            Assert.Empty(await Search(method, marker));
        }
        var page = await ListPage(list, owner);
        Assert.Empty(page.GetProperty("novels").EnumerateArray());
        Assert.Equal(0, page.GetProperty("novelsCount").GetInt32());

        // Nothing restores such a novel. Were one restored by hand, both lists would name its author as the novel page
        // does: the deleted account, «مستخدم محذوف» with its deleted-... user name and no photo.
        await using (var db = api.Db())
        {
            await db.Novels.IgnoreQueryFilters().Where(n => n.Id == novel.Id).ExecuteUpdateAsync(s => s.SetProperty(n => n.IsDeleted, false));
        }
        var pageAuthor = await NovelPageAuthor(novel);
        Assert.Equal(DeletedAccounts.DisplayName, pageAuthor.GetProperty("displayName").GetString());
        Assert.StartsWith(DeletedAccounts.UserNamePrefix, pageAuthor.GetProperty("userName").GetString());
        Assert.Equal(JsonValueKind.Null, pageAuthor.GetProperty("profilePhoto").ValueKind);
        Assert.Equal(pageAuthor.GetRawText(), Item(await Search("GET", marker), novel).GetProperty("author").GetRawText());
        Assert.Equal(pageAuthor.GetRawText(), Item((await ListPage(list, owner)).GetProperty("novels").EnumerateArray(), novel, "novelId").GetProperty("author").GetRawText());
    }
}
