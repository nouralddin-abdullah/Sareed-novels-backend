using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;

namespace Sareed_novels_backend.Tests.Integration;

/// <summary>GET /api/novel/by-id/{id}: the novel page by slug, under the same rules, for links that only have the id.</summary>
[Collection(ReaderApiCollection.Name)]
public class NovelByIdHttpTests(SardApiFactory api)
{
    [Fact]
    public async Task A_novel_by_id_is_the_same_page_as_by_slug()
    {
        var author = await api.SignUp();
        var reader = await api.SignUp();
        var novel = await api.AddNovel(author);

        foreach (var viewer in new[] { null, reader, author })
        {
            var bySlug = await api.Get(ReaderApi.BySlug(novel.Slug), viewer);
            var byId = await api.Get($"/api/novel/by-id/{novel.Id}", viewer);

            Assert.Equal(HttpStatusCode.OK, byId.StatusCode);
            Assert.Equal(await bySlug.Content.ReadAsStringAsync(), await byId.Content.ReadAsStringAsync());
        }

        var page = await (await api.Get($"/api/novel/by-id/{novel.Id}")).OkJson();
        Assert.Equal(novel.Id, page.GetProperty("id").GetGuid());
        Assert.Equal(novel.Slug, page.GetProperty("slug").GetString());
        Assert.Equal(author.Id, page.GetProperty("author").GetProperty("id").GetString());
        Assert.Single(page.GetProperty("genresList").EnumerateArray());
    }

    [Fact]
    public async Task A_renamed_novel_still_opens_by_id()
    {
        var author = await api.SignUp();
        var novel = await api.AddNovel(author, title: "الاسم القديم");

        var rename = await api.Send(HttpMethod.Patch, $"/api/myworks/{novel.Id}", author, JsonContent.Create(new { title = "الاسم الجديد" }));
        Assert.Equal(HttpStatusCode.OK, rename.StatusCode);

        Assert.Equal(HttpStatusCode.NotFound, (await api.Get(ReaderApi.BySlug(novel.Slug))).StatusCode);
        var page = await (await api.Get($"/api/novel/by-id/{novel.Id}")).OkJson();
        Assert.Equal("الاسم الجديد", page.GetProperty("title").GetString());
        var newSlug = page.GetProperty("slug").GetString()!;
        Assert.NotEqual(novel.Slug, newSlug);
        Assert.Equal(HttpStatusCode.OK, (await api.Get(ReaderApi.BySlug(newSlug))).StatusCode);
    }

    [Fact]
    public async Task A_draft_opens_for_its_author_only_by_id_and_by_slug()
    {
        var author = await api.SignUp();
        var reader = await api.SignUp();
        var draft = await api.AddNovel(author, isDraft: true);

        foreach (var outsider in new[] { null, reader })
        {
            Assert.Equal(HttpStatusCode.NotFound, (await api.Get($"/api/novel/by-id/{draft.Id}", outsider)).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await api.Get(ReaderApi.BySlug(draft.Slug), outsider)).StatusCode);
        }

        var byId = await api.Get($"/api/novel/by-id/{draft.Id}", author);
        var bySlug = await api.Get(ReaderApi.BySlug(draft.Slug), author);
        Assert.Equal(draft.Id, (await byId.OkJson()).GetProperty("id").GetGuid());
        Assert.Equal(await bySlug.Content.ReadAsStringAsync(), await byId.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Unknown_and_deleted_novels_are_not_found()
    {
        var author = await api.SignUp();
        var deleted = await api.AddNovel(author);
        await using (var db = api.Db())
        {
            await db.Novels.Where(n => n.Id == deleted.Id).ExecuteUpdateAsync(s => s.SetProperty(n => n.IsDeleted, true));
        }

        Assert.Equal(HttpStatusCode.NotFound, (await api.Get($"/api/novel/by-id/{Guid.NewGuid()}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await api.Get("/api/novel/by-id/not-a-guid")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await api.Get($"/api/novel/by-id/{deleted.Id}", author)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await api.Get(ReaderApi.BySlug(deleted.Slug), author)).StatusCode);
    }
}
