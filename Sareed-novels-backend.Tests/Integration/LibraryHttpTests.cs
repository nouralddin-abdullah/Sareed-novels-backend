using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Domain.Constants;
using Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Sareed_novels_backend.Middlewares;

namespace Sareed_novels_backend.Tests.Integration;

/// <summary>
/// #33: a reader removes a novel from her library (DELETE /api/library/novel/{id}), mutes one novel's new chapters
/// (PATCH /api/library/novel/{id}), and her library says so and when each novel's newest chapter came out. #45: and
/// how many chapters came out since she last read (newChaptersCount).
/// </summary>
[Collection(ReaderApiCollection.Name)]
public class LibraryHttpTests(SardApiFactory api)
{
    private async Task<(Novel Novel, Chapter Chapter)> NovelWithChapter(ApiUser author)
    {
        var novel = await api.AddNovel(author);
        var (chapter, _) = await api.AddChapter(novel, "<p>فقرة</p>");
        return (novel, chapter);
    }

    private Task Read(ApiUser reader, Chapter chapter) => Read(reader, chapter.Id);

    private async Task Read(ApiUser reader, Guid chapterId) =>
        (await api.Send(HttpMethod.Post, $"/api/library/track-progress/{chapterId}", reader)).EnsureSuccessStatusCode();

    /// <summary>A chapter the author writes in the editor (POST /api/novel/{id}/chapter), published or as a draft.</summary>
    private async Task<Guid> Write(ApiUser author, Novel novel, string status) =>
        (await (await api.Send(HttpMethod.Post, $"/api/novel/{novel.Id}/chapter", author,
            JsonContent.Create(new { status, title = "فصل " + Seed.Marker(), content = "<p>نص</p>" }))).OkJson())
        .GetProperty("id").GetGuid();

    /// <summary>The author saves a chapter with this status (PATCH), sending its title and text as the editor does.</summary>
    private async Task SetStatus(ApiUser author, Novel novel, Guid chapterId, string status) =>
        (await api.Send(HttpMethod.Patch, $"/api/novel/{novel.Id}/chapter/{chapterId}", author,
            JsonContent.Create(new { status, title = "فصل", content = "<p>نص</p>" }))).EnsureSuccessStatusCode();

    private async Task DeleteChapter(ApiUser author, Novel novel, Guid chapterId) =>
        (await api.Send(HttpMethod.Delete, $"/api/novel/{novel.Id}/chapter/{chapterId}", author))
        .EnsureSuccessStatusCode();

    /// <summary>When track-progress stored her last read of the novel, as the database has it (to 100 ns).</summary>
    private async Task<DateTime> StoredLastReadAt(ApiUser reader, Novel novel)
    {
        await using var db = api.Db();
        return await db.UserNovelProgress
            .Where(p => p.UserId == reader.Id && p.NovelId == novel.Id)
            .Select(p => p.LastReadAt)
            .SingleAsync();
    }

    /// <summary>
    /// Chapter <paramref name="index"/> of the novel, saved directly with this status and the time it came out
    /// (<see cref="Chapter.PublishedAt"/>; a draft with one was unpublished since).
    /// </summary>
    private async Task<Chapter> AddChapterPublishedAt(
        Novel novel, int index, DateTime? publishedAt, string status = ChapterStatuses.Published)
    {
        var chapter = Seed.Chapters(novel, 1, DateTime.UtcNow.AddDays(-30), status, startIndex: index).Single();
        chapter.PublishedAt = publishedAt;
        await using var db = api.Db();
        db.Chapters.Add(chapter);
        await db.SaveChangesAsync();
        return chapter;
    }

    /// <summary>A date of a library item as UTC: lastChapterPublishedAt says so ("Z"); lastReadAt is UTC without it.</summary>
    private static DateTime Utc(JsonElement item, string name) =>
        DateTime.Parse(item.GetProperty(name).GetString()!, CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal);

    private Task<HttpResponseMessage> Remove(ApiUser? reader, Guid novelId) =>
        api.Send(HttpMethod.Delete, $"/api/library/novel/{novelId}", reader);

    private Task<HttpResponseMessage> SetNotifications(ApiUser? reader, Guid novelId, bool notify) =>
        api.Send(HttpMethod.Patch, $"/api/library/novel/{novelId}", reader, JsonContent.Create(new { notifyNewChapters = notify }));

    /// <summary>The items of GET /api/library/reading-progress, by novel id.</summary>
    private async Task<Dictionary<Guid, JsonElement>> Library(ApiUser reader)
    {
        var page = await (await api.Get("/api/library/reading-progress?pageSize=100", reader)).OkJson();
        return page.GetProperty("items").EnumerateArray().ToDictionary(item => item.GetProperty("novelId").GetGuid());
    }

    /// <summary>GET /api/library/novel/{id}/progress: <c>progress</c>, or null when <c>hasProgress</c> is false.</summary>
    private async Task<JsonElement?> Progress(ApiUser reader, Guid novelId)
    {
        var body = await (await api.Get($"/api/library/novel/{novelId}/progress", reader)).OkJson();
        return body.GetProperty("hasProgress").GetBoolean() ? body.GetProperty("progress") : null;
    }

    private static async Task AssertNoContent(HttpResponseMessage response)
    {
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == HttpStatusCode.NoContent, $"{(int)response.StatusCode}: {body}");
        Assert.Empty(body);
    }

    private static bool Notifies(JsonElement item) => item.GetProperty("notifyNewChapters").GetBoolean();

    /// <summary>newChaptersCount of a library item or of progress, which must be a number (#45).</summary>
    private static int NewChapters(JsonElement item)
    {
        var count = item.GetProperty("newChaptersCount");
        Assert.Equal(JsonValueKind.Number, count.ValueKind);
        return count.GetInt32();
    }

    /// <summary>
    /// The item's newChaptersCount agrees with its lastChapterPublishedAt, both dates read as UTC as the app reads
    /// them: above 0 exactly when the newest chapter came out after her last read (#45).
    /// </summary>
    private static void AssertAgreesWithNewestChapter(JsonElement item)
    {
        var (newest, lastReadAt) = (item.GetProperty("lastChapterPublishedAt"), item.GetProperty("lastReadAt"));
        var newestIsNew = newest.ValueKind != JsonValueKind.Null
            && Utc(item, "lastChapterPublishedAt") > Utc(item, "lastReadAt");
        Assert.True(newestIsNew == (NewChapters(item) > 0),
            $"newChaptersCount {NewChapters(item)}, lastChapterPublishedAt {newest}, lastReadAt {lastReadAt}");
    }

    [Fact]
    public async Task Removing_a_novel_takes_it_out_of_her_library_and_leaves_other_readers_entries_alone()
    {
        var (author, reader, other) = (await api.SignUp(), await api.SignUp(), await api.SignUp());
        var (novel, chapter) = await NovelWithChapter(author);
        var (kept, keptChapter) = await NovelWithChapter(author);
        await Read(reader, chapter);
        await Read(reader, keptChapter);
        await Read(other, chapter);

        await AssertNoContent(await Remove(reader, novel.Id));

        Assert.Equal([kept.Id], (await Library(reader)).Keys);
        Assert.Null(await Progress(reader, novel.Id));
        Assert.NotNull(await Progress(reader, kept.Id));
        var others = Assert.Single(await Library(other));
        Assert.Equal((novel.Id, chapter.Id), (others.Key, others.Value.GetProperty("lastReadChapterId").GetGuid()));
        Assert.True(Notifies(others.Value));
        await using var db = api.Db();
        Assert.Equal(1, await db.Users.Where(u => u.Id == reader.Id).Select(u => u.LibraryNovelsCount).SingleAsync());
        Assert.Equal(1, await db.Users.Where(u => u.Id == other.Id).Select(u => u.LibraryNovelsCount).SingleAsync());
    }

    [Fact]
    public async Task Removing_a_novel_that_is_not_in_her_library_is_done_too()
    {
        var (author, reader) = (await api.SignUp(), await api.SignUp());
        var (novel, chapter) = await NovelWithChapter(author);
        var (neverRead, _) = await NovelWithChapter(author);
        await Read(reader, chapter);

        await AssertNoContent(await Remove(reader, neverRead.Id));
        await AssertNoContent(await Remove(reader, Guid.NewGuid()));
        await AssertNoContent(await Remove(reader, novel.Id));
        await AssertNoContent(await Remove(reader, novel.Id)); // again: already removed

        Assert.Empty(await Library(reader));
        await using var db = api.Db();
        Assert.Equal(0, await db.Users.Where(u => u.Id == reader.Id).Select(u => u.LibraryNovelsCount).SingleAsync());
    }

    [Fact]
    public async Task Muting_a_novel_turns_its_new_chapter_notifications_off_and_on_again()
    {
        var (author, reader) = (await api.SignUp(), await api.SignUp());
        var (novel, chapter) = await NovelWithChapter(author);
        var (other, otherChapter) = await NovelWithChapter(author);
        await Read(reader, chapter);
        await Read(reader, otherChapter);
        Assert.True(Notifies((await Library(reader))[novel.Id]));
        Assert.True(Notifies((await Progress(reader, novel.Id))!.Value));

        await AssertNoContent(await SetNotifications(reader, novel.Id, false));
        Assert.False(Notifies((await Library(reader))[novel.Id]));
        Assert.False(Notifies((await Progress(reader, novel.Id))!.Value));
        Assert.True(Notifies((await Library(reader))[other.Id])); // only that novel

        await AssertNoContent(await SetNotifications(reader, novel.Id, false)); // already off
        await Read(reader, chapter); // reading on keeps it muted
        Assert.False(Notifies((await Library(reader))[novel.Id]));

        await AssertNoContent(await SetNotifications(reader, novel.Id, true));
        Assert.True(Notifies((await Library(reader))[novel.Id]));
        Assert.True(Notifies((await Progress(reader, novel.Id))!.Value));
    }

    [Fact]
    public async Task Muting_a_novel_that_is_not_in_her_library_is_404_NotInLibrary()
    {
        var (author, reader, other) = (await api.SignUp(), await api.SignUp(), await api.SignUp());
        var (novel, chapter) = await NovelWithChapter(author);
        var (removed, removedChapter) = await NovelWithChapter(author);
        await Read(other, chapter); // someone else's entry is not hers
        await Read(reader, removedChapter);
        await AssertNoContent(await Remove(reader, removed.Id));

        foreach (var novelId in new[] { novel.Id, removed.Id, Guid.NewGuid() })
        {
            foreach (var notify in new[] { false, true })
            {
                var error = await (await SetNotifications(reader, novelId, notify)).Error(HttpStatusCode.NotFound);
                Assert.Equal("NotInLibrary", error.GetProperty("code").GetString());
                Assert.Equal("هذه الرواية ليست في مكتبتك.", error.GetProperty("message").GetString());
            }
        }

        Assert.Empty(await Library(reader)); // not added by the attempt
        Assert.True(Notifies(Assert.Single(await Library(other)).Value));
    }

    [Fact]
    public async Task Muting_without_a_value_is_refused_in_arabic()
    {
        var (author, reader) = (await api.SignUp(), await api.SignUp());
        var (novel, chapter) = await NovelWithChapter(author);
        await Read(reader, chapter);

        // No value, null, and an empty body.
        foreach (var body in new HttpContent[]
                 {
                     JsonContent.Create(new { }), JsonContent.Create(new { notifyNewChapters = (bool?)null }),
                     new StringContent("", Encoding.UTF8, "application/json")
                 })
        {
            var error = await (await api.Send(HttpMethod.Patch, $"/api/library/novel/{novel.Id}", reader, body)).Error(HttpStatusCode.BadRequest);
            Assert.Equal(ValidationProblems.Code, error.GetProperty("code").GetString());
            Assert.Matches(@"\p{IsArabic}", error.GetProperty("message").GetString());
        }

        Assert.True(Notifies((await Library(reader))[novel.Id])); // unchanged
    }

    [Fact]
    public async Task Removing_and_muting_need_a_signed_in_reader()
    {
        var (author, reader) = (await api.SignUp(), await api.SignUp());
        var (novel, chapter) = await NovelWithChapter(author);
        await Read(reader, chapter);

        Assert.Equal(HttpStatusCode.Unauthorized, (await Remove(null, novel.Id)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await SetNotifications(null, novel.Id, false)).StatusCode);

        Assert.True(Notifies(Assert.Single(await Library(reader)).Value));
    }

    [Fact]
    public async Task Reading_a_removed_novel_again_adds_it_back_with_notifications_on()
    {
        var (author, reader) = (await api.SignUp(), await api.SignUp());
        var (novel, chapter) = await NovelWithChapter(author);
        await Read(reader, chapter);
        await AssertNoContent(await SetNotifications(reader, novel.Id, false));
        await AssertNoContent(await Remove(reader, novel.Id));

        await Read(reader, chapter);

        Assert.True(Notifies((await Library(reader))[novel.Id]));
        Assert.True(Notifies((await Progress(reader, novel.Id))!.Value));
    }

    [Fact]
    public async Task The_library_says_when_the_newest_published_chapter_came_out_in_utc_and_null_without_one()
    {
        var (author, reader) = (await api.SignUp(), await api.SignUp());
        var novel = await api.AddNovel(author);
        var emptied = await api.AddNovel(author);
        var newest = new DateTime(2026, 9, 20, 10, 3, 0, DateTimeKind.Utc);
        var published = Seed.Chapters(novel, 2, newest.AddDays(-3));
        published[1].PublishedAt = newest; // written with the first, out three days later
        var draft = Seed.Chapters(novel, 1, newest.AddDays(1), status: "Draft", startIndex: 3).Single();
        var emptiedChapter = Seed.Chapters(emptied, 1, newest).Single();
        await using (var db = api.Db())
        {
            db.Chapters.AddRange(published);
            db.Chapters.AddRange(draft, emptiedChapter);
            await db.SaveChangesAsync();
        }
        await Read(reader, published[0]);
        await Read(reader, emptiedChapter);
        await using (var db = api.Db())
        {
            // Unpublished after she read it: still in her library, with no published chapter.
            await db.Chapters.Where(c => c.Id == emptiedChapter.Id).ExecuteUpdateAsync(s => s.SetProperty(c => c.Status, "Draft"));
        }

        var library = await Library(reader);

        var raw = library[novel.Id].GetProperty("lastChapterPublishedAt").GetString()!;
        Assert.EndsWith("Z", raw); // UTC, and says so
        var parsed = DateTime.Parse(raw, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
        Assert.Equal((newest, DateTimeKind.Utc), (parsed, parsed.Kind));
        Assert.Equal(2, library[novel.Id].GetProperty("totalChapters").GetInt32());
        Assert.Equal(JsonValueKind.Null, library[emptied.Id].GetProperty("lastChapterPublishedAt").ValueKind);
        Assert.Equal(0, library[emptied.Id].GetProperty("totalChapters").GetInt32());
    }

    [Fact]
    public async Task A_draft_written_before_her_last_read_and_published_after_it_is_new_to_her_once()
    {
        var (author, reader) = (await api.SignUp(), await api.SignUp());
        var novel = await api.AddNovel(author);
        var first = await Write(author, novel, "Published");
        var draft = await Write(author, novel, "Draft");
        await Read(reader, first); // after the draft was written
        await SetStatus(author, novel, draft, "Published"); // after her read

        var item = (await Library(reader))[novel.Id];
        var (lastReadAt, cameOut) = (Utc(item, "lastReadAt"), Utc(item, "lastChapterPublishedAt"));
        await using (var db = api.Db())
        {
            var stored = await db.Chapters.Where(c => c.Id == draft).Select(c => new { c.CreatedAt, c.PublishedAt }).SingleAsync();
            Assert.True(stored.CreatedAt < lastReadAt, $"written {stored.CreatedAt:O}, last read {lastReadAt:O}");
            Assert.Equal(stored.PublishedAt, cameOut); // when it was published, not when it was written
        }
        Assert.True(cameOut > lastReadAt, $"out {cameOut:O}, last read {lastReadAt:O}"); // «فصول جديدة»
        Assert.Equal(1, NewChapters(item)); // «فصل جديد»
        Assert.Equal(2, item.GetProperty("totalChapters").GetInt32());

        // She reads it. The author unpublishes it and publishes it again: it keeps when it first came out, so it
        // isn't new to her a second time.
        await Read(reader, draft);
        await SetStatus(author, novel, draft, "Draft");
        await SetStatus(author, novel, draft, "Published");

        item = (await Library(reader))[novel.Id];
        Assert.Equal(cameOut, Utc(item, "lastChapterPublishedAt"));
        Assert.True(Utc(item, "lastChapterPublishedAt") < Utc(item, "lastReadAt"));
        Assert.Equal(0, NewChapters(item));
    }

    [Fact]
    public async Task A_new_chapter_notifies_and_pushes_to_the_readers_who_did_not_mute_it_on_both_publishing_paths()
    {
        var (author, listening, muted) = (await api.SignUp(), await api.SignUp(), await api.SignUp());
        var (novel, chapter) = await NovelWithChapter(author);
        await Read(listening, chapter);
        await Read(muted, chapter);
        await AssertNoContent(await SetNotifications(muted, novel.Id, false));
        await using (var db = api.Db())
        {
            db.UserDevices.AddRange(new[] { listening, muted }.Select(r => new UserDevice
            {
                Id = Guid.NewGuid(), UserId = r.Id, Token = "token-" + Guid.NewGuid().ToString("N"), Platform = DevicePlatforms.Android,
                CreatedAt = DateTime.UtcNow, LastSeenAt = DateTime.UtcNow
            }));
            await db.SaveChangesAsync();
        }

        // Published when created (CreateChapterCommandHandler).
        var created = (await (await api.Send(HttpMethod.Post, $"/api/novel/{novel.Id}/chapter", author,
            JsonContent.Create(new { status = "Published", title = "الفصل الثاني", content = "<p>نص</p>" }))).OkJson()).GetProperty("id").GetGuid();
        await AssertOnlyTheListenerIsTold(created);

        // Saved as a draft, published later (UpdateChapterCommandHandler).
        var drafted = (await (await api.Send(HttpMethod.Post, $"/api/novel/{novel.Id}/chapter", author,
            JsonContent.Create(new { status = "Draft", title = "الفصل الثالث", content = "<p>نص</p>" }))).OkJson()).GetProperty("id").GetGuid();
        (await api.Send(HttpMethod.Patch, $"/api/novel/{novel.Id}/chapter/{drafted}", author,
            JsonContent.Create(new { status = "Published", title = "الفصل الثالث", content = "<p>نص</p>" }))).EnsureSuccessStatusCode();
        await AssertOnlyTheListenerIsTold(drafted);

        async Task AssertOnlyTheListenerIsTold(Guid chapterId)
        {
            // The notifications are written in the background, all readers in one save: once the listener has hers,
            // the muted reader would have had hers too.
            for (var waited = 0; waited < 150 && await Told(listening, chapterId) == (0, 0); waited++)
            {
                await Task.Delay(100);
            }

            Assert.Equal((1, 1), await Told(listening, chapterId)); // a notification, and a push to her phone
            Assert.Equal((0, 0), await Told(muted, chapterId));
        }

        async Task<(int Notifications, int Pushes)> Told(ApiUser reader, Guid chapterId)
        {
            await using var db = api.Db();
            var notifications = db.Notifications.Where(n =>
                n.UserId == reader.Id && n.RelatedEntityId == chapterId && n.Type == NotificationType.NewChapterInLibrary);
            var pushes = db.PushOutbox.Join(notifications, o => o.NotificationId, n => n.Id, (o, _) => o);
            return (await notifications.CountAsync(), await pushes.CountAsync());
        }
    }

    // #45: newChaptersCount, the chapters that came out since she last read, for «N فصول جديدة».

    [Fact]
    public async Task newChaptersCount_is_0_while_nothing_came_out_since_she_last_read()
    {
        var (author, reader) = (await api.SignUp(), await api.SignUp());
        var novel = await api.AddNovel(author);
        var first = await Write(author, novel, "Published");
        await Write(author, novel, "Published");
        await Write(author, novel, "Published");
        await Read(reader, first); // the first of three: two left to read, none new
        await Write(author, novel, "Draft"); // written since, not out

        var item = (await Library(reader))[novel.Id];

        Assert.Equal(0, NewChapters(item));
        Assert.Equal(1, item.GetProperty("lastReadChapterNumber").GetInt32());
        Assert.Equal(3, item.GetProperty("totalChapters").GetInt32());
        AssertAgreesWithNewestChapter(item);
        Assert.Equal(0, NewChapters((await Progress(reader, novel.Id))!.Value));
    }

    [Fact]
    public async Task newChaptersCount_counts_the_chapters_that_came_out_after_her_last_read()
    {
        var (author, reader) = (await api.SignUp(), await api.SignUp());
        var novel = await api.AddNovel(author);
        var other = await api.AddNovel(author);
        var first = await Write(author, novel, "Published");
        var draft = await Write(author, novel, "Draft"); // written before she reads
        await Read(reader, first);
        await Read(reader, await Write(author, other, "Published"));

        // After her read: two chapters created published, and the draft published, which counts from when it was
        // published, not from when it was written; and one in her other novel.
        await Write(author, novel, "Published");
        await Write(author, novel, "Published");
        await SetStatus(author, novel, draft, "Published");
        await Write(author, other, "Published");

        var library = await Library(reader);

        Assert.Equal(3, NewChapters(library[novel.Id]));
        Assert.Equal(4, library[novel.Id].GetProperty("totalChapters").GetInt32());
        Assert.Equal(1, NewChapters(library[other.Id])); // each novel counts its own chapters
        Assert.All(library.Values, AssertAgreesWithNewestChapter);
        Assert.Equal(3, NewChapters((await Progress(reader, novel.Id))!.Value));
        Assert.Equal(1, NewChapters((await Progress(reader, other.Id))!.Value));
    }

    [Fact]
    public async Task Drafts_and_chapters_unpublished_or_deleted_since_they_came_out_are_not_counted()
    {
        var (author, reader) = (await api.SignUp(), await api.SignUp());
        var novel = await api.AddNovel(author);
        var first = await Write(author, novel, "Published");
        await Read(reader, first);

        await Write(author, novel, "Draft");
        var unpublished = await Write(author, novel, "Published");
        await SetStatus(author, novel, unpublished, "Draft");
        var deleted = await Write(author, novel, "Published");
        await DeleteChapter(author, novel, deleted);

        var item = (await Library(reader))[novel.Id];
        Assert.Equal(0, NewChapters(item));
        Assert.Equal(1, item.GetProperty("totalChapters").GetInt32());
        AssertAgreesWithNewestChapter(item);

        // Published again, it counts from when it first came out, which is after her last read: new to her.
        await SetStatus(author, novel, unpublished, "Published");
        item = (await Library(reader))[novel.Id];
        Assert.Equal(1, NewChapters(item));
        AssertAgreesWithNewestChapter(item);
    }

    [Fact]
    public async Task A_chapter_that_came_out_the_instant_she_last_read_is_not_new()
    {
        var (author, reader) = (await api.SignUp(), await api.SignUp());
        var novel = await api.AddNovel(author);
        await Read(reader, await Write(author, novel, "Published"));
        var lastReadAt = await StoredLastReadAt(reader, novel);

        // Out at the very instant she read (both times are stored to 100 ns) and a tick before it: not after it.
        await AddChapterPublishedAt(novel, 2, lastReadAt.AddTicks(-1));
        await AddChapterPublishedAt(novel, 3, lastReadAt);

        var item = (await Library(reader))[novel.Id];
        Assert.Equal(lastReadAt, Utc(item, "lastReadAt"));
        Assert.Equal(lastReadAt, Utc(item, "lastChapterPublishedAt")); // the newest is not later than her read
        Assert.Equal(0, NewChapters(item));
        AssertAgreesWithNewestChapter(item);

        // A tick after it is.
        await AddChapterPublishedAt(novel, 4, lastReadAt.AddTicks(1));
        item = (await Library(reader))[novel.Id];
        Assert.Equal(1, NewChapters(item));
        AssertAgreesWithNewestChapter(item);
    }

    [Fact]
    public async Task Reading_any_chapter_again_brings_newChaptersCount_back_to_0()
    {
        var (author, reader) = (await api.SignUp(), await api.SignUp());
        var novel = await api.AddNovel(author);
        var first = await Write(author, novel, "Published");
        await Read(reader, first);
        var second = await Write(author, novel, "Published");
        await Write(author, novel, "Published");
        Assert.Equal(2, NewChapters((await Library(reader))[novel.Id]));

        // She opens the first of the two: counted from this read, neither is new any more, the one she hasn't opened
        // included (it came out before this read).
        await Read(reader, second);
        var item = (await Library(reader))[novel.Id];
        Assert.Equal(0, NewChapters(item));
        AssertAgreesWithNewestChapter(item);
        Assert.Equal(0, NewChapters((await Progress(reader, novel.Id))!.Value));

        // Another comes out; opening an earlier chapter is a read too.
        await Write(author, novel, "Published");
        Assert.Equal(1, NewChapters((await Library(reader))[novel.Id]));
        await Read(reader, first);
        item = (await Library(reader))[novel.Id];
        Assert.Equal(0, NewChapters(item));
        AssertAgreesWithNewestChapter(item);
    }

    [Fact]
    public async Task newChaptersCount_is_above_0_exactly_when_lastChapterPublishedAt_is_later_than_lastReadAt()
    {
        var (author, reader) = (await api.SignUp(), await api.SignUp());
        var lastReadAt = new DateTime(2026, 9, 20, 10, 3, 0, DateTimeKind.Utc).AddTicks(1234567);
        var expected = new Dictionary<Guid, int>();

        // A novel in her library, read at lastReadAt, whose chapters came out at these times (null: never published).
        async Task<Novel> Entry(int newChapters, params (DateTime? PublishedAt, string Status)[] chapters)
        {
            var novel = await api.AddNovel(author);
            var added = new List<Chapter>();
            for (var i = 0; i < chapters.Length; i++)
            {
                added.Add(await AddChapterPublishedAt(novel, i + 1, chapters[i].PublishedAt, chapters[i].Status));
            }
            await using var db = api.Db();
            db.UserNovelProgress.Add(Seed.Progress(new User { Id = reader.Id }, added[0], 1, lastReadAt));
            await db.SaveChangesAsync();
            expected[novel.Id] = newChapters;
            return novel;
        }

        const string published = ChapterStatuses.Published, draft = ChapterStatuses.Draft;
        var (before, hour) = (lastReadAt.AddDays(-1), TimeSpan.FromHours(1));
        // Nothing published any more: it came out after her read and was unpublished; a draft never published.
        var nothingPublished = await Entry(0, (lastReadAt + hour, draft), (null, draft));
        await Entry(0, (before, published), (before.AddHours(1), published));
        await Entry(0, (before, published), (lastReadAt, published)); // the newest came out the instant she read
        await Entry(1, (before, published), (lastReadAt.AddTicks(1), published));
        // Three after her read, besides a draft and a chapter unpublished since it came out (the latest of them).
        await Entry(3, (before, published), (lastReadAt + hour, published), (lastReadAt + 2 * hour, published),
            (lastReadAt + 3 * hour, published), (null, draft), (lastReadAt + 4 * hour, draft));
        // A published chapter without a publish date (only code older than PublishedAt could leave one, #39) is in
        // neither: not the newest, not new.
        await Entry(0, (before, published), (null, published));

        var library = await Library(reader);

        Assert.Equal(expected, library.ToDictionary(item => item.Key, item => NewChapters(item.Value)));
        Assert.All(library.Values, AssertAgreesWithNewestChapter);
        Assert.Equal(JsonValueKind.Null, library[nothingPublished.Id].GetProperty("lastChapterPublishedAt").ValueKind);
        foreach (var (novelId, newChapters) in expected)
        {
            // The same in the novel's progress.
            Assert.Equal(newChapters, NewChapters((await Progress(reader, novelId))!.Value));
        }
    }
}
