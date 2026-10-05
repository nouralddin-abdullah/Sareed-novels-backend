using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Application.Chapters.Scheduling;
using Domain.Constants;
using Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Sareed_novels_backend.Tests.Integration;

/// <summary>
/// #77 through the API as the apps use it: <c>publishAt</c> on creating and saving a chapter (left out keeps a schedule,
/// null cancels it, a time that has come is refused in Arabic), the author's chapter list and chapter with
/// <c>wordsCount</c> and <c>publishAt</c>, my works with the novel's <c>wordsCount</c>, and a due chapter coming out on
/// the next request that reads its novel (the scheduler isn't running here, as when the host has stopped the app).
/// </summary>
[Collection(ReaderApiCollection.Name)]
public class ScheduledChaptersHttpTests(SardApiFactory api)
{
    [Fact]
    public async Task A_scheduled_draft_shows_its_time_and_words_to_its_author_and_comes_out_on_the_next_read_of_its_novel()
    {
        var author = await api.SignUp();
        var novel = await api.AddNovel(author);
        var (first, _) = await api.AddChapter(novel, "<p>الفصل الأول</p>");
        var reader = await api.SignUp();
        await InLibrary(reader, first);
        var publishAt = Truncated(DateTime.UtcNow.AddDays(1));

        var created = await (await Create(author, novel, ChapterStatuses.Draft, "<p>بِسْمِ اللَّهِ، نَبْدَأُ.</p><p>* * *</p>", publishAt)).OkJson();
        var chapterId = created.GetProperty("id").GetGuid();

        // The author sees when it publishes itself, and its words.
        Assert.Equal(publishAt, Utc(created, "publishAt"));
        Assert.Equal(3, created.GetProperty("wordsCount").GetInt32());
        var mine = Items(await (await api.Get($"/api/myworks/{novel.Id}/chapters", author)).OkJson());
        Assert.Equal((publishAt, 3), (Utc(mine[chapterId], "publishAt"), mine[chapterId].GetProperty("wordsCount").GetInt32()));
        Assert.Equal(JsonValueKind.Null, mine[first.Id].GetProperty("publishAt").ValueKind);
        Assert.Equal(JsonValueKind.Null, mine[first.Id].GetProperty("wordsCount").ValueKind); // stored before word counts, not counted yet
        var one = await (await api.Get($"/api/myworks/{novel.Id}/chapters/{chapterId}", author)).OkJson();
        Assert.Equal((publishAt, 3), (Utc(one, "publishAt"), one.GetProperty("wordsCount").GetInt32()));

        // Readers don't: the draft isn't listed, and the list's items have neither field.
        var listed = await (await api.Get($"/api/novel/{novel.Id}/chapter")).OkJson();
        Assert.Equal([first.Id], Ids(listed));
        Assert.False(listed[0].TryGetProperty("publishAt", out _));
        Assert.False(listed[0].TryGetProperty("wordsCount", out _));

        // The host stopped the app while it fell due: the next request reading the novel finds it due.
        await using (var db = api.Db())
        {
            await db.Chapters.Where(c => c.Id == chapterId).ExecuteUpdateAsync(s => s.SetProperty(c => c.PublishAt, DateTime.UtcNow.AddMinutes(-3)));
        }
        var before = DateTime.UtcNow;
        listed = await (await api.Get($"/api/novel/{novel.Id}/chapter")).OkJson();

        Assert.Equal([first.Id, chapterId], Ids(listed));
        var stored = await Stored(chapterId);
        Assert.Equal((ChapterStatuses.Published, (DateTime?)null), (stored.Status, stored.PublishAt));
        Assert.InRange(stored.PublishedAt!.Value, before.AddSeconds(-1), DateTime.UtcNow);
        await using (var db = api.Db())
        {
            Assert.Equal(2, await db.Novels.Where(n => n.Id == novel.Id).Select(n => n.ChapterCount).SingleAsync());
        }
        Assert.Equal(1, await Announcements(reader, chapterId));

        // Reading it again, by its page or its chapter, publishes nothing more.
        (await api.Get(ReaderApi.BySlug(novel.Slug))).EnsureSuccessStatusCode();
        (await api.Get($"/api/novel/{novel.Id}/chapter/{chapterId}", reader)).EnsureSuccessStatusCode();
        Assert.Equal(1, await Announcements(reader, chapterId));
        Assert.Equal(stored.PublishedAt, (await Stored(chapterId)).PublishedAt);
    }

    [Fact]
    public async Task Saving_without_publishAt_keeps_the_schedule_null_cancels_it_and_a_time_that_has_come_is_refused_in_arabic()
    {
        var author = await api.SignUp();
        var novel = await api.AddNovel(author);
        var publishAt = Truncated(DateTime.UtcNow.AddHours(5));
        var chapterId = (await (await Create(author, novel, ChapterStatuses.Draft, "<p>نص</p>", publishAt)).OkJson()).GetProperty("id").GetGuid();

        // The web's save: title, text and status, no publishAt.
        await (await Save(author, novel, chapterId, new { title = "فصل", status = ChapterStatuses.Draft, content = "<p>نص معدل</p>" })).OkJson();
        Assert.Equal(publishAt, (await Stored(chapterId)).PublishAt);

        var past = await Save(author, novel, chapterId,
            new { title = "فصل", status = ChapterStatuses.Draft, content = "<p>نص لا يحفظ</p>", publishAt = DateTime.UtcNow.AddMinutes(-1) });
        var refused = await past.Error(HttpStatusCode.BadRequest);
        Assert.Equal((ChapterSchedule.InPastCode, "موعد النشر يجب أن يكون في المستقبل"),
            (refused.GetProperty("code").GetString(), refused.GetProperty("message").GetString()));
        Assert.Equal((publishAt, (int?)2), ((await Stored(chapterId)).PublishAt, (await Stored(chapterId)).WordsCount));

        // An offset is read as the same moment in UTC.
        var later = publishAt.AddHours(1);
        await (await Save(author, novel, chapterId, new { title = "فصل", content = "<p>نص معدل</p>", publishAt = later.AddHours(3).ToString("yyyy-MM-ddTHH:mm:ss+03:00") })).OkJson();
        Assert.Equal(later, (await Stored(chapterId)).PublishAt);

        await (await Save(author, novel, chapterId, new { title = "فصل", content = "<p>نص معدل</p>", publishAt = (DateTime?)null })).OkJson();
        Assert.Null((await Stored(chapterId)).PublishAt);
        var mine = Items(await (await api.Get($"/api/myworks/{novel.Id}/chapters", author)).OkJson());
        Assert.Equal(JsonValueKind.Null, mine[chapterId].GetProperty("publishAt").ValueKind);

        // Creating: a time that has come, or a schedule on a chapter created published.
        var createdLate = await (await Create(author, novel, ChapterStatuses.Draft, "<p>نص</p>", DateTime.UtcNow.AddSeconds(-30))).Error(HttpStatusCode.BadRequest);
        Assert.Equal((ChapterSchedule.InPastCode, "موعد النشر يجب أن يكون في المستقبل"),
            (createdLate.GetProperty("code").GetString(), createdLate.GetProperty("message").GetString()));
        var createdPublished = await (await Create(author, novel, ChapterStatuses.Published, "<p>نص</p>", publishAt)).Error(HttpStatusCode.BadRequest);
        Assert.Equal((ChapterSchedule.NotDraftCode, "يمكن تحديد موعد نشر للمسودات فقط"),
            (createdPublished.GetProperty("code").GetString(), createdPublished.GetProperty("message").GetString()));
        await using var db = api.Db();
        Assert.Equal(1, await db.Chapters.CountAsync(c => c.NovelId == novel.Id));
    }

    [Fact]
    public async Task A_patch_may_hold_publishAt_alone_or_with_a_status_without_the_text_or_a_revision()
    {
        var author = await api.SignUp();
        var novel = await api.AddNovel(author);
        var chapterId = (await (await Create(author, novel, ChapterStatuses.Draft, "<p>نص الفصل</p>")).OkJson()).GetProperty("id").GetGuid();
        var publishAt = Truncated(DateTime.UtcNow.AddDays(2));

        // publishAt alone, set and then cancelled; a baseRevision sent with it isn't checked, and the revision stays.
        Assert.Equal(1, await Revision(await Save(author, novel, chapterId, new { publishAt = publishAt.ToString("yyyy-MM-ddTHH:mm:ssZ") })));
        var scheduled = await Stored(chapterId);
        Assert.Equal((ChapterStatuses.Draft, (DateTime?)publishAt, 1), (scheduled.Status, scheduled.PublishAt, scheduled.Revision));
        Assert.Equal(1, await Revision(await Save(author, novel, chapterId, new { publishAt = (DateTime?)null, baseRevision = 99 })));
        Assert.Null((await Stored(chapterId)).PublishAt);

        // With a status alone: unpublished and scheduled in one save.
        Assert.Equal(1, await Revision(await Save(author, novel, chapterId, new { status = ChapterStatuses.Published })));
        Assert.Equal(1, await Revision(await Save(author, novel, chapterId,
            new { status = ChapterStatuses.Draft, publishAt = publishAt.ToString("yyyy-MM-ddTHH:mm:ssZ") })));
        var rescheduled = await Stored(chapterId);
        Assert.Equal((ChapterStatuses.Draft, (DateTime?)publishAt, 1), (rescheduled.Status, rescheduled.PublishAt, rescheduled.Revision));

        // A title or text still needs both; and a body with nothing to save is refused.
        await AssertInvalid(await Save(author, novel, chapterId, new { title = "فصل", publishAt = (DateTime?)null }), "اكتب نص الفصل");
        await AssertInvalid(await Save(author, novel, chapterId, new { baseRevision = 1 }),
            "أرسل حالة الفصل أو موعد نشره، أو عنوانه ونصه");

        // A dry run checks the schedule as the save would, and saves nothing.
        var dryRun = await api.Send(HttpMethod.Patch, $"/api/novel/{novel.Id}/chapter/{chapterId}?dryRun=true", author,
            JsonContent.Create(new { publishAt = DateTime.UtcNow.AddMinutes(-1) }));
        Assert.Equal(ChapterSchedule.InPastCode, (await dryRun.Error(HttpStatusCode.BadRequest)).GetProperty("code").GetString());
        Assert.Equal(publishAt, (await Stored(chapterId)).PublishAt);

        // The title and text with it move the revision as any text save does.
        Assert.Equal(2, await Revision(await Save(author, novel, chapterId,
            new { title = "فصل", content = "<p>نص جديد</p>", publishAt = (DateTime?)null, baseRevision = 1 })));
        Assert.Null((await Stored(chapterId)).PublishAt);
    }

    [Fact]
    public async Task My_works_give_each_novels_words_over_all_its_chapters_drafts_included()
    {
        var author = await api.SignUp();
        var novel = await api.AddNovel(author);
        var empty = await api.AddNovel(author);
        (await Create(author, novel, ChapterStatuses.Published, "<p>واحد اثنان ثلاثة</p>")).EnsureSuccessStatusCode();
        var draft = (await (await Create(author, novel, ChapterStatuses.Draft, "<p>أربعة،</p><p>خمسة!</p>")).OkJson()).GetProperty("id").GetGuid();

        Assert.Equal((5, 0), (await MyWorksWords(author, novel), await MyWorksWords(author, empty)));
        Assert.Equal(5, (await (await api.Get($"/api/myworks/{novel.Id}", author)).OkJson()).GetProperty("wordsCount").GetInt32());

        // Saving the draft's text counts it again.
        await (await Save(author, novel, draft, new { title = "فصل", content = "<p>أربعة خمسة ستة سبعة</p>" })).OkJson();
        Assert.Equal(7, await MyWorksWords(author, novel));

        // A chapter from before word counts, not counted yet: the novel's total isn't known yet either.
        await api.AddChapter(novel, "<p>فصل قديم</p>");
        Assert.Null(await MyWorksWords(author, novel));
        Assert.Equal(JsonValueKind.Null,
            (await (await api.Get($"/api/myworks/{novel.Id}", author)).OkJson()).GetProperty("wordsCount").ValueKind);
    }

    private Task<HttpResponseMessage> Create(ApiUser author, Novel novel, string status, string content, DateTime? publishAt = null) =>
        api.Send(HttpMethod.Post, $"/api/novel/{novel.Id}/chapter", author, JsonContent.Create(publishAt is null
            ? (object)new { status, title = "فصل " + Seed.Marker(), content }
            : new { status, title = "فصل " + Seed.Marker(), content, publishAt = publishAt.Value.ToString("yyyy-MM-ddTHH:mm:ssZ") }));

    private Task<HttpResponseMessage> Save(ApiUser author, Novel novel, Guid chapterId, object body) =>
        api.Send(HttpMethod.Patch, $"/api/novel/{novel.Id}/chapter/{chapterId}", author, JsonContent.Create(body));

    /// <summary>A save's answer, which must be a success: the chapter's revision after it.</summary>
    private static async Task<int> Revision(HttpResponseMessage saved)
    {
        var body = await saved.OkJson();
        Assert.True(body.GetProperty("success").GetBoolean());
        return body.GetProperty("revision").GetInt32();
    }

    private static async Task AssertInvalid(HttpResponseMessage response, string message)
    {
        var error = await response.Error(HttpStatusCode.BadRequest);
        Assert.Equal(("ValidationFailed", message), (error.GetProperty("code").GetString(), error.GetProperty("message").GetString()));
    }

    private async Task<Chapter> Stored(Guid chapterId)
    {
        await using var db = api.Db();
        return await db.Chapters.AsNoTracking().SingleAsync(c => c.Id == chapterId);
    }

    private async Task InLibrary(ApiUser reader, Chapter chapter)
    {
        await using var db = api.Db();
        db.UserNovelProgress.Add(Seed.Progress(new User { Id = reader.Id }, chapter, 1, DateTime.UtcNow));
        await db.SaveChangesAsync();
    }

    /// <summary>
    /// The reader's new-chapter notifications for this chapter. They are sent in the background after the publish: this
    /// waits for one (up to 10 s), then a moment more, so a second would show.
    /// </summary>
    private async Task<int> Announcements(ApiUser reader, Guid chapterId)
    {
        async Task<int> Count()
        {
            await using var db = api.Db();
            return await db.Notifications.CountAsync(n =>
                n.UserId == reader.Id && n.RelatedEntityId == chapterId && n.Type == NotificationType.NewChapterInLibrary);
        }

        for (var waited = 0; waited < 100 && await Count() == 0; waited++)
        {
            await Task.Delay(100);
        }
        await Task.Delay(300);
        return await Count();
    }

    private async Task<int?> MyWorksWords(ApiUser author, Novel novel)
    {
        var page = await (await api.Get("/api/myworks?pageSize=100", author)).OkJson();
        var work = Assert.Single(page.GetProperty("items").EnumerateArray(), w => w.GetProperty("id").GetGuid() == novel.Id);
        var words = work.GetProperty("wordsCount");
        return words.ValueKind == JsonValueKind.Null ? null : words.GetInt32();
    }

    private static DateTime Truncated(DateTime utc) => new(utc.Ticks - utc.Ticks % TimeSpan.TicksPerSecond, DateTimeKind.Utc);

    /// <summary>A time the API sends, which must be UTC with "Z".</summary>
    private static DateTime Utc(JsonElement item, string field)
    {
        var text = item.GetProperty(field).GetString()!;
        Assert.EndsWith("Z", text);
        return DateTime.Parse(text, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal);
    }

    private static List<Guid> Ids(JsonElement list) => list.EnumerateArray().Select(c => c.GetProperty("id").GetGuid()).ToList();

    private static Dictionary<Guid, JsonElement> Items(JsonElement list) => list.EnumerateArray().ToDictionary(c => c.GetProperty("id").GetGuid());
}
