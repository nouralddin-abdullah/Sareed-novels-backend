using Infrastructure.Repositories;
using Microsoft.EntityFrameworkCore;

namespace Sareed_novels_backend.Tests.Integration;

public class ChapterCountTests(SqlServerDatabase database) : IClassFixture<SqlServerDatabase>
{
    [Fact]
    public async Task Refreshing_sets_the_count_of_published_chapters_from_the_chapters_table_and_heals_drift()
    {
        await using (var seed = database.CreateContext())
        {
            var author = Seed.User();
            var novel = Seed.Novel(author, "رواية " + Seed.Marker());
            novel.ChapterCount = -1; // what the cross-novel delete bug left behind
            seed.Users.Add(author);
            seed.Novels.Add(novel);
            seed.Chapters.AddRange(Seed.Chapters(novel, 3, DateTime.UtcNow, status: "Published"));
            seed.Chapters.AddRange(Seed.Chapters(novel, 2, DateTime.UtcNow, status: "Draft", startIndex: 4));
            await seed.SaveChangesAsync();
            novelId = novel.Id;
        }

        var updatedAt = new DateTime(2026, 9, 25, 12, 0, 0, DateTimeKind.Utc);
        await using (var db = database.CreateContext())
        {
            var repository = new NovelsRepository(db);
            var tracked = await repository.GetOne(novelId);

            await repository.RefreshChapterCountAsync(novelId, updatedAt);

            // The tracked copy is refreshed too, so a later SaveChanges cannot write the stale count back. The two
            // drafts don't count (#25): readers see three chapters in the list.
            Assert.Equal(3, tracked!.ChapterCount);
        }

        await using var check = database.CreateContext();
        var stored = await check.Novels.SingleAsync(n => n.Id == novelId);
        Assert.Equal(3, stored.ChapterCount);
        Assert.Equal(updatedAt, stored.LastUpdatedAt);
    }

    private Guid novelId;
}

/// <summary>
/// A novel's chapterCount is what readers can open (#25): it used to count drafts, so a novel said 16 chapters and
/// listed 14. Creating, publishing, unpublishing and deleting chapters through the API keep it right.
/// </summary>
[Collection(ReaderApiCollection.Name)]
public class PublishedChapterCountHttpTests(SardApiFactory api)
{
    [Fact]
    public async Task The_count_follows_publishing_unpublishing_and_deleting_chapters()
    {
        var author = await api.SignUp();
        var novel = await api.AddNovel(author);
        var chapters = $"/api/novel/{novel.Id}/chapter";

        async Task<Guid> Create(string status) =>
            (await (await api.Send(HttpMethod.Post, chapters, author,
                System.Net.Http.Json.JsonContent.Create(new { status, title = "فصل " + Seed.Marker(), content = "<p>نص</p>" }))).OkJson())
            .GetProperty("id").GetGuid();
        // The editor sends the title and text with every save.
        Task<HttpResponseMessage> SetStatus(Guid chapterId, string status) =>
            api.Send(HttpMethod.Patch, $"{chapters}/{chapterId}", author,
                System.Net.Http.Json.JsonContent.Create(new { status, title = "فصل", content = "<p>نص</p>" }));
        async Task AssertCount(int expected)
        {
            var page = await (await api.Get($"/api/novel/by-id/{novel.Id}")).OkJson();
            Assert.Equal(expected, page.GetProperty("chapterCount").GetInt32());
            // The count matches the chapter list readers get.
            Assert.Equal(expected, (await (await api.Get(chapters)).OkJson()).GetArrayLength());
        }

        var first = await Create("Published");
        var draft = await Create("Draft");
        await AssertCount(1);

        (await SetStatus(draft, "Published")).EnsureSuccessStatusCode();
        await AssertCount(2);

        (await SetStatus(first, "Draft")).EnsureSuccessStatusCode();
        await AssertCount(1);

        (await api.Send(HttpMethod.Delete, $"{chapters}/{first}", author)).EnsureSuccessStatusCode(); // a draft now
        await AssertCount(1);
        (await api.Send(HttpMethod.Delete, $"{chapters}/{draft}", author)).EnsureSuccessStatusCode();
        await AssertCount(0);
    }
}
