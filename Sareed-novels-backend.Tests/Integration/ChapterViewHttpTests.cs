using System.Net;
using System.Net.Http.Headers;
using Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Sareed_novels_backend.Tests.Integration;

/// <summary>
/// Offline reading (#25): a chapter downloaded with prefetch isn't counted as read on the download day; the app counts
/// the read with POST .../view when the reader opens it, once per visitor per day like the reader itself.
/// </summary>
[Collection(ReaderApiCollection.Name)]
public class ChapterViewHttpTests(SardApiFactory api)
{
    /// <summary>A phone: its own address (so its own anonymous visitor key) and the app's HTTP client's user agent.</summary>
    private sealed class Device(HttpClient client)
    {
        public async Task<HttpResponseMessage> Send(HttpMethod method, string url, ApiUser? user = null, string? prefetchHeader = null)
        {
            var request = new HttpRequestMessage(method, url);
            request.Headers.UserAgent.ParseAdd("Dart/3.5 (dart:io)");
            if (user != null)
            {
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", user.Token);
            }
            if (prefetchHeader != null)
            {
                request.Headers.Add("X-Sard-Prefetch", prefetchHeader);
            }
            return await client.SendAsync(request);
        }
    }

    private Device NewDevice() => new(api.Client());

    private static string Url(Chapter chapter) => $"/api/novel/{chapter.NovelId}/chapter/{chapter.Id}";

    private async Task<int> Views(Chapter chapter)
    {
        await using var db = api.Db();
        return await db.Chapters.Where(c => c.Id == chapter.Id).Select(c => c.ViewsCount).SingleAsync();
    }

    /// <summary>Waits (up to 10 s) for the reader's background count to reach <paramref name="expected"/>.</summary>
    private async Task WaitForViews(Chapter chapter, int expected)
    {
        for (var waited = 0; waited < 100 && await Views(chapter) < expected; waited++)
        {
            await Task.Delay(100);
        }
        Assert.Equal(expected, await Views(chapter));
    }

    private async Task<Chapter> PublishedChapter(ApiUser author) => (await api.AddChapter(await api.AddNovel(author), "<p>فقرة</p>")).Chapter;

    [Fact]
    public async Task A_prefetch_is_not_counted_and_opening_the_chapter_still_is()
    {
        var chapter = await PublishedChapter(await api.SignUp());
        var reader = await api.SignUp();
        var phone = NewDevice();

        foreach (var response in new[]
                 {
                     await phone.Send(HttpMethod.Get, Url(chapter) + "?prefetch=true", reader),
                     await phone.Send(HttpMethod.Get, Url(chapter) + "?prefetch=1"),
                     await phone.Send(HttpMethod.Get, Url(chapter), reader, prefetchHeader: "1"),
                     await NewDevice().Send(HttpMethod.Get, Url(chapter), prefetchHeader: "true"),
                 })
        {
            // The whole chapter, as for a read (its paragraph in chapter format v1, #74).
            var body = await response.OkJson();
            Assert.Equal("فقرة", Assert.Single(body.GetProperty("paragraphs").EnumerateArray()).GetProperty("content").GetString());
        }
        await Task.Delay(500);
        Assert.Equal(0, await Views(chapter));

        // Opening it (no prefetch, or prefetch=false) counts, in the background, as before.
        (await phone.Send(HttpMethod.Get, Url(chapter) + "?prefetch=false", reader)).EnsureSuccessStatusCode();
        await WaitForViews(chapter, 1);
    }

    [Fact]
    public async Task A_view_counts_once_a_day_per_visitor_like_the_reader()
    {
        var chapter = await PublishedChapter(await api.SignUp());
        var reader = await api.SignUp();
        var (phone, otherPhone) = (NewDevice(), NewDevice());

        Assert.Equal(HttpStatusCode.NoContent, (await phone.Send(HttpMethod.Post, Url(chapter) + "/view", reader)).StatusCode);
        Assert.Equal(1, await Views(chapter)); // counted before the answer, not in the background
        // Again that day, from another phone signed in as the same reader, or after reading it online: still one.
        Assert.Equal(HttpStatusCode.NoContent, (await otherPhone.Send(HttpMethod.Post, Url(chapter) + "/view", reader)).StatusCode);
        (await phone.Send(HttpMethod.Get, Url(chapter), reader)).EnsureSuccessStatusCode();
        await Task.Delay(500);
        Assert.Equal(1, await Views(chapter));

        // A signed-out reader counts by device, once too.
        Assert.Equal(HttpStatusCode.NoContent, (await otherPhone.Send(HttpMethod.Post, Url(chapter) + "/view")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await otherPhone.Send(HttpMethod.Post, Url(chapter) + "/view")).StatusCode);
        Assert.Equal(2, await Views(chapter));
    }

    [Fact]
    public async Task The_author_crawlers_and_chapters_readers_cannot_open_are_not_counted()
    {
        var author = await api.SignUp();
        var novel = await api.AddNovel(author);
        var (chapter, _) = await api.AddChapter(novel, "<p>فقرة</p>");
        var phone = NewDevice();

        Assert.Equal(HttpStatusCode.NoContent, (await phone.Send(HttpMethod.Post, Url(chapter) + "/view", author)).StatusCode);
        var crawler = new HttpRequestMessage(HttpMethod.Post, Url(chapter) + "/view");
        crawler.Headers.UserAgent.ParseAdd("Mozilla/5.0 (compatible; Googlebot/2.1; +http://www.google.com/bot.html)");
        Assert.Equal(HttpStatusCode.NoContent, (await api.Client().SendAsync(crawler)).StatusCode);
        Assert.Equal(0, await Views(chapter));

        // As the reader answers: a draft, a chapter through another novel, and nothing at all are missing chapters.
        Chapter draft;
        await using (var db = api.Db())
        {
            draft = Seed.Chapters(novel, 1, DateTime.UtcNow, status: "Draft", startIndex: 2).Single();
            db.Chapters.Add(draft);
            await db.SaveChangesAsync();
        }
        var otherNovel = await api.AddNovel(author);
        foreach (var url in new[]
                 {
                     Url(draft) + "/view",
                     $"/api/novel/{otherNovel.Id}/chapter/{chapter.Id}/view",
                     $"/api/novel/{novel.Id}/chapter/{Guid.NewGuid()}/view",
                 })
        {
            var error = await (await phone.Send(HttpMethod.Post, url)).Error(HttpStatusCode.NotFound);
            Assert.Equal("ChapterNotFound", error.GetProperty("code").GetString());
        }
        var noNovel = await (await phone.Send(HttpMethod.Post, $"/api/novel/{Guid.NewGuid()}/chapter/{chapter.Id}/view")).Error(HttpStatusCode.NotFound);
        Assert.Equal("NovelNotFound", noNovel.GetProperty("code").GetString());
        Assert.Equal(0, await Views(draft));
    }

    [Fact]
    public async Task A_chapter_locked_for_the_caller_is_not_counted()
    {
        var author = await api.SignUp();
        var novel = await api.AddNovel(author);
        var (chapter, _) = await api.AddChapter(novel, "<p>فقرة</p>");
        await using (var db = api.Db())
        {
            // Early access from the first published chapter on: every chapter is locked for non-subscribers.
            await db.Chapters.Where(c => c.Id == chapter.Id).ExecuteUpdateAsync(s => s.SetProperty(c => c.PublishedChapterSequence, 1));
            db.NovelPrivileges.Add(new NovelPrivilege
            {
                Id = Guid.NewGuid(), NovelId = novel.Id, IsEnabled = true, SubscriptionCost = 200, CurrentLockedCount = 5, PrivilegeStartSequence = 1
            });
            await db.SaveChangesAsync();
        }
        var reader = await api.SignUp();
        Assert.True((await (await NewDevice().Send(HttpMethod.Get, Url(chapter) + "?prefetch=true", reader)).OkJson()).GetProperty("isLocked").GetBoolean());

        Assert.Equal(HttpStatusCode.NoContent, (await NewDevice().Send(HttpMethod.Post, Url(chapter) + "/view", reader)).StatusCode);

        Assert.Equal(0, await Views(chapter));
    }
}
