using System.Data.Common;
using Application.Chapters.Commands.UpdateChapter;
using Application.Chapters.Scheduling;
using Domain.Constants;
using Domain.Exceptions;
using Infrastructure.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Sareed_novels_backend.Tests.Integration;

/// <summary>
/// #77: a draft scheduled with <c>publishAt</c> comes out at its time through the author's own publish path (status,
/// when it came out, sequences, chapter count, last update, privilege window, readers' notifications), once, without
/// moving its revision or when it was last saved. A scheduled publish holds the chapter as an author's save does (#75),
/// so two runs, or a run and the author's save, run one after the other, the second reading what the first stored:
/// the chapter is published once and readers are told once. Editing it keeps the schedule, null cancels it, publishing
/// it by hand clears it, and only a draft is scheduled, for a time to come. Through the real handlers and the scheduler
/// on a clock (<see cref="ScheduleDesk"/>). Other tests' chapters share the database, so each test looks at its own
/// chapters, not at how many a run published.
/// </summary>
public class ScheduledChaptersTests(SqlServerDatabase database) : IClassFixture<SqlServerDatabase>, IAsyncLifetime
{
    private static readonly DateTime Start = new(2026, 10, 5, 8, 0, 0, DateTimeKind.Utc);

    private readonly ScheduleDesk desk = new(database, Start);

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync() => await desk.DisposeAsync();

    [Fact]
    public async Task A_scheduled_draft_comes_out_at_its_time_once()
    {
        var (author, novel) = await desk.SeedNovel();
        var chapter = await desk.Create(author, novel, ChapterStatuses.Draft, publishAt: Start.AddHours(2));

        var draft = await desk.Stored(chapter);
        Assert.Equal((ChapterStatuses.Draft, Start.AddHours(2), (DateTime?)null), (draft.Status, draft.PublishAt, draft.PublishedAt));

        // Not before its time.
        desk.Clock.Advance(TimeSpan.FromHours(2) - TimeSpan.FromSeconds(1));
        await desk.PublishDue();
        Assert.Equal(ChapterStatuses.Draft, (await desk.Stored(chapter)).Status);
        Assert.Equal(0, await desk.Announced(chapter, expected: 0));

        // Due: it comes out when the run publishes it.
        desk.Clock.Advance(TimeSpan.FromSeconds(30));
        var cameOut = desk.Clock.UtcNow;
        await desk.PublishDue();

        var published = await desk.Stored(chapter);
        Assert.Equal((ChapterStatuses.Published, (DateTime?)null, cameOut, 1),
            (published.Status, published.PublishAt, published.PublishedAt, published.PublishedChapterSequence));
        var stored = await desk.StoredNovel(novel.Id);
        Assert.Equal((1, cameOut), (stored.ChapterCount, stored.LastUpdatedAt));
        Assert.Equal(1, await desk.Announced(chapter));
        Assert.Equal(1, desk.WindowExtensions(novel.Id));

        // Later runs find nothing to do.
        desk.Clock.Advance(TimeSpan.FromMinutes(1));
        await desk.PublishDue();
        await desk.PublishDue();
        Assert.Equal(1, await desk.Announced(chapter));
        Assert.Equal(1, desk.WindowExtensions(novel.Id));
        Assert.Equal(cameOut, (await desk.Stored(chapter)).PublishedAt);
    }

    [Fact]
    public async Task On_schedule_or_by_hand_a_chapter_comes_out_the_same()
    {
        var (handAuthor, byHand) = await desk.SeedNovel();
        var (scheduleAuthor, onSchedule) = await desk.SeedNovel();
        await desk.Create(handAuthor, byHand, ChapterStatuses.Published);
        await desk.Create(scheduleAuthor, onSchedule, ChapterStatuses.Published);
        var handDraft = await desk.Create(handAuthor, byHand, ChapterStatuses.Draft);
        var scheduledDraft = await desk.Create(scheduleAuthor, onSchedule, ChapterStatuses.Draft, publishAt: Start.AddHours(1));

        desk.Clock.Advance(TimeSpan.FromHours(1));
        Assert.True((await desk.Save(handAuthor, byHand, handDraft, ChapterStatuses.Published)).Success);
        await desk.PublishDue();

        var (hand, scheduled) = (await desk.Stored(handDraft), await desk.Stored(scheduledDraft));
        Assert.Equal((hand.Status, hand.PublishAt, hand.PublishedAt, hand.PublishedChapterSequence),
            (scheduled.Status, scheduled.PublishAt, scheduled.PublishedAt, scheduled.PublishedChapterSequence));
        Assert.Equal((ChapterStatuses.Published, 2), (scheduled.Status, scheduled.PublishedChapterSequence));
        var (handNovel, scheduledNovel) = (await desk.StoredNovel(byHand.Id), await desk.StoredNovel(onSchedule.Id));
        Assert.Equal((handNovel.ChapterCount, handNovel.LastUpdatedAt), (scheduledNovel.ChapterCount, scheduledNovel.LastUpdatedAt));
        Assert.Equal((1, 1), (await desk.Announced(handDraft), await desk.Announced(scheduledDraft)));
        Assert.Equal((2, 2), (desk.WindowExtensions(byHand.Id), desk.WindowExtensions(onSchedule.Id)));
    }

    [Fact]
    public async Task Publishing_on_schedule_moves_neither_the_revision_nor_when_the_chapter_was_last_saved()
    {
        var (author, novel) = await desk.SeedNovel();
        var chapter = await desk.Create(author, novel, ChapterStatuses.Draft, publishAt: Start.AddHours(1));
        desk.Clock.Advance(TimeSpan.FromMinutes(10));
        Assert.Equal(2, (await desk.Save(author, novel, chapter, status: null, content: "<p>نص معدل</p>")).Revision);

        desk.Clock.Advance(TimeSpan.FromHours(1));
        await desk.PublishDue();

        var published = await desk.Stored(chapter);
        Assert.Equal((ChapterStatuses.Published, desk.Clock.UtcNow, 2, Start.AddMinutes(10)),
            (published.Status, published.PublishedAt!.Value, published.Revision, published.UpdatedAt));
    }

    [Fact]
    public async Task Two_runs_at_the_same_moment_publish_a_due_draft_once()
    {
        var (author, novel) = await desk.SeedNovel();
        var chapter = await desk.Create(author, novel, ChapterStatuses.Draft, publishAt: Start.AddHours(1));
        desk.Clock.Advance(TimeSpan.FromHours(1));

        // A run holds the chapter to publish it; a second run (a request reading the novel, another instance during a
        // deploy) has found it due too, and waits for it. It then finds it published.
        var first = desk.ScheduleCommands.HoldWithChapter(chapter);
        var firstRun = desk.PublishDue();
        await first.Held;
        var secondRun = desk.PublishDue();
        await AssertWaits(secondRun);
        Assert.Equal(ChapterStatuses.Draft, (await desk.Stored(chapter)).Status);
        first.Release();
        await Task.WhenAll(firstRun, secondRun);

        Assert.Equal(ChapterStatuses.Published, (await desk.Stored(chapter)).Status);
        Assert.Equal(1, await desk.Announced(chapter));
        Assert.Equal(1, desk.WindowExtensions(novel.Id));
        Assert.Equal(1, (await desk.StoredNovel(novel.Id)).ChapterCount);

        // A run that found it due, but holds it only after another run published it, does nothing either.
        var next = await desk.Create(author, novel, ChapterStatuses.Draft, publishAt: Start.AddHours(1).AddMinutes(1));
        desk.Clock.Advance(TimeSpan.FromMinutes(1));
        var otherRan = false;
        desk.ScheduleCommands.Before(command => TakesTheLockOf(command, next), async () =>
        {
            await desk.PublishDue();
            otherRan = true;
        });
        await desk.PublishDue();
        Assert.True(otherRan);
        Assert.Equal(1, await desk.Announced(next));
        Assert.Equal(2, desk.WindowExtensions(novel.Id));

        // And truly at once.
        var last = await desk.Create(author, novel, ChapterStatuses.Draft, publishAt: Start.AddHours(1).AddMinutes(2));
        desk.Clock.Advance(TimeSpan.FromMinutes(1));
        await Task.WhenAll(desk.PublishDue(), desk.PublishDue(), desk.PublishDue());
        Assert.Equal(1, await desk.Announced(last));
        Assert.Equal(3, desk.WindowExtensions(novel.Id));
        Assert.Equal(3, (await desk.StoredNovel(novel.Id)).ChapterCount);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task A_scheduled_publish_and_the_authors_save_of_the_chapter_run_one_after_the_other(bool scheduleFirst)
    {
        // The author saves new text (no status) at the moment the chapter falls due. Whichever holds the chapter first,
        // the other waits, then reads what it stored: the chapter comes out once, with the new text.
        var (author, novel) = await desk.SeedNovel();
        var chapter = await desk.Create(author, novel, ChapterStatuses.Draft, publishAt: Start.AddHours(1));
        desk.Clock.Advance(TimeSpan.FromHours(1));
        var authorsSave = new CommandHook();

        var gate = (scheduleFirst ? desk.ScheduleCommands : authorsSave).HoldWithChapter(chapter);
        Task<UpdateChapterResult> Save() => desk.Save(author, novel, chapter, status: null, content: "<p>نص معدل من ثلاث كلمات</p>",
            interceptors: authorsSave);
        var firstTask = scheduleFirst ? (Task)desk.PublishDue() : Save();
        await gate.Held;
        var secondTask = scheduleFirst ? Save() : (Task)desk.PublishDue();
        await AssertWaits(secondTask);
        gate.Release();
        await Task.WhenAll(firstTask, secondTask);

        var stored = await desk.Stored(chapter);
        Assert.Equal((ChapterStatuses.Published, (DateTime?)null, (int?)1, (int?)5, 2),
            (stored.Status, stored.PublishAt, stored.PublishedChapterSequence, stored.WordsCount, stored.Revision));
        Assert.Equal(desk.Clock.UtcNow, stored.PublishedAt);
        Assert.Equal(1, (await desk.StoredNovel(novel.Id)).ChapterCount);
        Assert.Equal(1, await desk.Announced(chapter));
        Assert.Equal(1, desk.WindowExtensions(novel.Id));
    }

    [Fact]
    public async Task An_author_publishing_a_due_draft_the_scheduler_holds_waits_and_publishes_nothing_more()
    {
        var (author, novel) = await desk.SeedNovel();
        var chapter = await desk.Create(author, novel, ChapterStatuses.Draft, publishAt: Start.AddHours(1));
        desk.Clock.Advance(TimeSpan.FromHours(1));

        var schedule = desk.ScheduleCommands.HoldWithChapter(chapter);
        var run = desk.PublishDue();
        await schedule.Held;
        var save = desk.Save(author, novel, chapter, ChapterStatuses.Published);
        await AssertWaits(save);
        schedule.Release();
        await run;

        Assert.True((await save).Success);
        var stored = await desk.Stored(chapter);
        Assert.Equal((ChapterStatuses.Published, (DateTime?)null, (int?)1), (stored.Status, stored.PublishAt, stored.PublishedChapterSequence));
        Assert.Equal(1, await desk.Announced(chapter));
        Assert.Equal(1, desk.WindowExtensions(novel.Id));
        Assert.Equal(1, (await desk.StoredNovel(novel.Id)).ChapterCount);
    }

    [Fact]
    public async Task The_scheduler_waits_for_an_author_publishing_a_due_draft_and_publishes_nothing_more()
    {
        var (author, novel) = await desk.SeedNovel();
        var chapter = await desk.Create(author, novel, ChapterStatuses.Draft, publishAt: Start.AddHours(1));
        desk.Clock.Advance(TimeSpan.FromHours(1));

        var authorsSave = new CommandHook();
        var hold = authorsSave.HoldWithChapter(chapter);
        var save = desk.Save(author, novel, chapter, ChapterStatuses.Published, interceptors: authorsSave);
        await hold.Held;
        var run = desk.PublishDue();
        await AssertWaits(run);
        hold.Release();
        Assert.True((await save).Success);
        await run;

        var stored = await desk.Stored(chapter);
        Assert.Equal((ChapterStatuses.Published, (DateTime?)null, (DateTime?)desk.Clock.UtcNow, (int?)1),
            (stored.Status, stored.PublishAt, stored.PublishedAt, stored.PublishedChapterSequence));
        Assert.Equal(1, await desk.Announced(chapter));
        Assert.Equal(1, desk.WindowExtensions(novel.Id));
        Assert.Equal(1, (await desk.StoredNovel(novel.Id)).ChapterCount);
    }

    [Fact]
    public async Task A_run_holding_a_chapter_its_author_published_since_it_found_it_due_publishes_nothing()
    {
        var (author, novel) = await desk.SeedNovel();
        var chapter = await desk.Create(author, novel, ChapterStatuses.Draft, publishAt: Start.AddHours(1));
        desk.Clock.Advance(TimeSpan.FromHours(1));

        // The run has found the chapter due; before it holds it, its author publishes it.
        var authorPublished = false;
        desk.ScheduleCommands.Before(command => TakesTheLockOf(command, chapter), async () =>
        {
            Assert.True((await desk.Save(author, novel, chapter, ChapterStatuses.Published)).Success);
            authorPublished = true;
        });
        await desk.PublishDue();

        Assert.True(authorPublished);
        var stored = await desk.Stored(chapter);
        Assert.Equal((ChapterStatuses.Published, (DateTime?)null, (int?)1), (stored.Status, stored.PublishAt, stored.PublishedChapterSequence));
        Assert.Equal(1, await desk.Announced(chapter));
        Assert.Equal(1, desk.WindowExtensions(novel.Id));
        Assert.Equal(1, (await desk.StoredNovel(novel.Id)).ChapterCount);
    }

    [Fact]
    public async Task A_save_sending_draft_right_after_the_scheduled_publish_makes_it_a_draft_again_and_nothing_is_counted_twice()
    {
        // The status a save sends is stored, the last one winning, as between two saves (ChapterComesOutTests): the
        // web's editor sends the status it shows. Waiting for the scheduled publish, the save then unpublishes it. What
        // follows each change follows it, and readers were told once.
        var (author, novel) = await desk.SeedNovel();
        var chapter = await desk.Create(author, novel, ChapterStatuses.Draft, publishAt: Start.AddHours(1));
        desk.Clock.Advance(TimeSpan.FromHours(1));

        var schedule = desk.ScheduleCommands.HoldWithChapter(chapter);
        var run = desk.PublishDue();
        await schedule.Held;
        var authorsSave = new CommandHook();
        var saveHolds = authorsSave.HoldWithChapter(chapter);
        var save = desk.Save(author, novel, chapter, ChapterStatuses.Draft, interceptors: authorsSave);
        await AssertWaits(save);
        schedule.Release();
        // The save has the chapter once the publish is stored; it goes on once the publish has done all it does, so the
        // unpublish's own recalculations come after the publish's.
        await saveHolds.Held;
        await run;
        saveHolds.Release();
        Assert.True((await save).Success);

        var stored = await desk.Stored(chapter);
        Assert.Equal((ChapterStatuses.Draft, (DateTime?)null, (DateTime?)desk.Clock.UtcNow, (int?)null),
            (stored.Status, stored.PublishAt, stored.PublishedAt, stored.PublishedChapterSequence));
        Assert.Equal(0, (await desk.StoredNovel(novel.Id)).ChapterCount);
        Assert.Equal(1, await desk.Announced(chapter));

        // Its schedule went with the publish: it stays a draft.
        desk.Clock.Advance(TimeSpan.FromHours(1));
        await desk.PublishDue();
        Assert.Equal(ChapterStatuses.Draft, (await desk.Stored(chapter)).Status);
        Assert.Equal(1, await desk.Announced(chapter));
        Assert.Equal(1, desk.WindowExtensions(novel.Id));
    }

    [Fact]
    public async Task A_save_sending_draft_just_before_the_scheduled_publish_keeps_the_schedule()
    {
        // The web's save of a scheduled draft sends the status it shows, «Draft»: it doesn't touch the schedule, so the
        // run waiting for the save publishes the chapter after it, with its new text.
        var (author, novel) = await desk.SeedNovel();
        var chapter = await desk.Create(author, novel, ChapterStatuses.Draft, publishAt: Start.AddHours(1));
        desk.Clock.Advance(TimeSpan.FromHours(1));

        var authorsSave = new CommandHook();
        var hold = authorsSave.HoldWithChapter(chapter);
        var save = desk.Save(author, novel, chapter, ChapterStatuses.Draft, content: "<p>نص أخير</p>", interceptors: authorsSave);
        await hold.Held;
        var run = desk.PublishDue();
        await AssertWaits(run);
        hold.Release();
        Assert.True((await save).Success);
        await run;

        var stored = await desk.Stored(chapter);
        Assert.Equal((ChapterStatuses.Published, (DateTime?)null, (int?)2), (stored.Status, stored.PublishAt, stored.WordsCount));
        Assert.Equal(1, await desk.Announced(chapter));
        Assert.Equal(1, (await desk.StoredNovel(novel.Id)).ChapterCount);
    }

    [Fact]
    public async Task A_schedule_alone_or_with_a_status_needs_no_text_nor_revision_and_moves_no_revision()
    {
        var (author, novel) = await desk.SeedNovel();
        var chapter = await desk.Create(author, novel, ChapterStatuses.Draft);
        desk.Clock.Advance(TimeSpan.FromMinutes(5));

        // publishAt alone: no title, text or baseRevision (one sent isn't checked, as with a status alone, #75).
        var scheduled = await desk.Save(author, novel, chapter, status: null, setsSchedule: true, publishAt: Start.AddHours(2),
            content: null, title: null, baseRevision: 99);
        Assert.Equal((true, 1), (scheduled.Success, scheduled.Revision));
        var stored = await desk.Stored(chapter);
        Assert.Equal((ChapterStatuses.Draft, (DateTime?)Start.AddHours(2), 1, Start.AddMinutes(5), (int?)2),
            (stored.Status, stored.PublishAt, stored.Revision, stored.UpdatedAt, stored.WordsCount));

        // Moved, then cancelled with null, alone.
        Assert.True((await desk.Save(author, novel, chapter, status: null, setsSchedule: true, publishAt: Start.AddHours(3),
            content: null, title: null)).Success);
        Assert.Equal(Start.AddHours(3), (await desk.Stored(chapter)).PublishAt);
        Assert.True((await desk.Save(author, novel, chapter, status: null, setsSchedule: true, publishAt: null,
            content: null, title: null)).Success);
        Assert.Null((await desk.Stored(chapter)).PublishAt);

        // With a status alone: published, unpublished and scheduled in one save.
        Assert.True((await desk.Save(author, novel, chapter, ChapterStatuses.Published, content: null, title: null)).Success);
        var unscheduled = await desk.Save(author, novel, chapter, ChapterStatuses.Draft, setsSchedule: true, publishAt: Start.AddHours(4),
            content: null, title: null, baseRevision: 99);
        Assert.Equal((true, 1), (unscheduled.Success, unscheduled.Revision));
        stored = await desk.Stored(chapter);
        Assert.Equal((ChapterStatuses.Draft, (DateTime?)Start.AddHours(4), 1, "نص الفصل"),
            (stored.Status, stored.PublishAt, stored.Revision, await Text(chapter)));
    }

    [Fact]
    public async Task An_edit_keeps_the_schedule_null_cancels_it_and_a_new_time_moves_it()
    {
        var (author, novel) = await desk.SeedNovel();
        var chapter = await desk.Create(author, novel, ChapterStatuses.Draft, publishAt: Start.AddHours(1));

        // The web's save (title, text and the status it shows) and a save without the status keep it.
        Assert.True((await desk.Save(author, novel, chapter, ChapterStatuses.Draft, content: "<p>نص معدل أطول قليلًا</p>")).Success);
        Assert.True((await desk.Save(author, novel, chapter, status: null, content: "<p>نص معدل أطول قليلًا</p>")).Success);
        var edited = await desk.Stored(chapter);
        Assert.Equal((ChapterStatuses.Draft, (DateTime?)Start.AddHours(1), (int?)4), (edited.Status, edited.PublishAt, edited.WordsCount));

        // A new time moves it.
        Assert.True((await desk.Save(author, novel, chapter, ChapterStatuses.Draft, setsSchedule: true, publishAt: Start.AddHours(3))).Success);
        Assert.Equal(Start.AddHours(3), (await desk.Stored(chapter)).PublishAt);
        desk.Clock.Advance(TimeSpan.FromHours(2));
        await desk.PublishDue();
        Assert.Equal(ChapterStatuses.Draft, (await desk.Stored(chapter)).Status);

        // Null cancels it: it stays a draft however late it gets.
        Assert.True((await desk.Save(author, novel, chapter, ChapterStatuses.Draft, setsSchedule: true, publishAt: null)).Success);
        Assert.Null((await desk.Stored(chapter)).PublishAt);
        desk.Clock.Advance(TimeSpan.FromDays(2));
        await desk.PublishDue();
        await desk.ReadChapters(novel.Id, () => Task.CompletedTask);
        Assert.Equal(ChapterStatuses.Draft, (await desk.Stored(chapter)).Status);
        Assert.Equal(0, await desk.Announced(chapter, expected: 0));
    }

    [Fact]
    public async Task Publishing_a_scheduled_draft_by_hand_clears_its_schedule()
    {
        var (author, novel) = await desk.SeedNovel();
        var chapter = await desk.Create(author, novel, ChapterStatuses.Draft, publishAt: Start.AddHours(1));

        desk.Clock.Advance(TimeSpan.FromMinutes(10));
        Assert.True((await desk.Save(author, novel, chapter, ChapterStatuses.Published)).Success);
        var published = await desk.Stored(chapter);
        Assert.Equal((ChapterStatuses.Published, (DateTime?)null, (DateTime?)Start.AddMinutes(10)),
            (published.Status, published.PublishAt, published.PublishedAt));

        // Its old time comes and goes: nothing more.
        desk.Clock.Advance(TimeSpan.FromHours(1));
        await desk.PublishDue();
        Assert.Equal(1, await desk.Announced(chapter));
        Assert.Equal(1, desk.WindowExtensions(novel.Id));

        // Unpublished later, it isn't scheduled again.
        Assert.True((await desk.Save(author, novel, chapter, ChapterStatuses.Draft)).Success);
        await desk.PublishDue();
        var unpublished = await desk.Stored(chapter);
        Assert.Equal((ChapterStatuses.Draft, (DateTime?)null), (unpublished.Status, unpublished.PublishAt));
    }

    [Fact]
    public async Task A_time_that_has_come_is_refused_in_arabic_and_nothing_is_saved()
    {
        var (author, novel) = await desk.SeedNovel();

        foreach (var at in new[] { Start, Start.AddMinutes(-1), Start.AddDays(-30) })
        {
            var refused = await Assert.ThrowsAsync<BadRequestException>(() => desk.Create(author, novel, ChapterStatuses.Draft, publishAt: at));
            Assert.Equal((ChapterSchedule.InPastCode, "موعد النشر يجب أن يكون في المستقبل"), (refused.Code, refused.Message));
        }
        await using (var db = database.CreateContext())
        {
            Assert.False(await db.Chapters.AnyAsync(c => c.NovelId == novel.Id));
        }

        var chapter = await desk.Create(author, novel, ChapterStatuses.Draft, publishAt: Start.AddHours(1));
        var edit = await Assert.ThrowsAsync<BadRequestException>(() => desk.Save(author, novel, chapter, ChapterStatuses.Draft,
            setsSchedule: true, publishAt: Start.AddMinutes(-5), content: "<p>نص جديد لا يحفظ</p>"));

        Assert.Equal((ChapterSchedule.InPastCode, "موعد النشر يجب أن يكون في المستقبل"), (edit.Code, edit.Message));
        var stored = await desk.Stored(chapter);
        Assert.Equal((Start.AddHours(1), (int?)2), (stored.PublishAt, stored.WordsCount));
        await using var check = database.CreateContext();
        Assert.Equal("نص الفصل", await check.ChapterParagraphs.Where(p => p.ChapterId == chapter).Select(p => p.Content).SingleAsync());
    }

    [Fact]
    public async Task Only_a_draft_is_scheduled()
    {
        var (author, novel) = await desk.SeedNovel();

        var createdPublished = await Assert.ThrowsAsync<BadRequestException>(
            () => desk.Create(author, novel, ChapterStatuses.Published, publishAt: Start.AddHours(1)));
        Assert.Equal((ChapterSchedule.NotDraftCode, "يمكن تحديد موعد نشر للمسودات فقط"), (createdPublished.Code, createdPublished.Message));

        // A published chapter can't take a schedule, and a save can't publish a draft and schedule it.
        var published = await desk.Create(author, novel, ChapterStatuses.Published);
        var draft = await desk.Create(author, novel, ChapterStatuses.Draft);
        foreach (var (chapter, status) in new[] { (published, (string?)null), (published, ChapterStatuses.Published), (draft, ChapterStatuses.Published) })
        {
            var refused = await Assert.ThrowsAsync<BadRequestException>(
                () => desk.Save(author, novel, chapter, status, setsSchedule: true, publishAt: Start.AddHours(1)));
            Assert.Equal((ChapterSchedule.NotDraftCode, "يمكن تحديد موعد نشر للمسودات فقط"), (refused.Code, refused.Message));
        }
        Assert.Equal(ChapterStatuses.Draft, (await desk.Stored(draft)).Status);
        Assert.Null((await desk.Stored(published)).PublishAt);

        // Unpublished by the same save, it can be: it comes back at its time, and readers, told when it first came
        // out, aren't told again.
        Assert.True((await desk.Save(author, novel, published, ChapterStatuses.Draft, setsSchedule: true, publishAt: Start.AddHours(1))).Success);
        var unpublished = await desk.Stored(published);
        Assert.Equal((ChapterStatuses.Draft, (DateTime?)Start.AddHours(1)), (unpublished.Status, unpublished.PublishAt));
        desk.Clock.Advance(TimeSpan.FromHours(1));
        await desk.PublishDue();
        var back = await desk.Stored(published);
        Assert.Equal((ChapterStatuses.Published, (DateTime?)Start), (back.Status, back.PublishedAt));
        Assert.Equal(1, await desk.Announced(published));

        // A save is checked against the chapter as it is then: an app that still shows the chapter as a scheduled draft
        // can't schedule it again once the schedule has published it.
        var late = await Assert.ThrowsAsync<BadRequestException>(() => desk.Save(author, novel, published, status: null,
            setsSchedule: true, publishAt: Start.AddHours(5), content: null, title: null));
        Assert.Equal(ChapterSchedule.NotDraftCode, late.Code);
        Assert.Equal((ChapterStatuses.Published, (DateTime?)null), ((await desk.Stored(published)).Status, (await desk.Stored(published)).PublishAt));
    }

    [Fact]
    public async Task Chapters_of_a_novel_due_together_come_out_in_order_with_their_sequences()
    {
        var (author, novel) = await desk.SeedNovel();
        var first = await desk.Create(author, novel, ChapterStatuses.Published);
        var second = await desk.Create(author, novel, ChapterStatuses.Draft, publishAt: Start.AddHours(1));
        var third = await desk.Create(author, novel, ChapterStatuses.Draft, publishAt: Start.AddHours(1));

        // The app was stopped meanwhile: both are due at its next run.
        desk.Clock.Advance(TimeSpan.FromHours(5));
        await desk.PublishDue();

        Assert.Equal([1, 2, 3], new[]
        {
            (await desk.Stored(first)).PublishedChapterSequence, (await desk.Stored(second)).PublishedChapterSequence,
            (await desk.Stored(third)).PublishedChapterSequence
        });
        Assert.Equal(3, (await desk.StoredNovel(novel.Id)).ChapterCount);
        Assert.Equal((1, 1), (await desk.Announced(second), await desk.Announced(third)));
        Assert.Equal(3, desk.WindowExtensions(novel.Id));
    }

    [Fact]
    public async Task A_deleted_novels_scheduled_draft_is_not_published()
    {
        var (author, novel) = await desk.SeedNovel();
        var chapter = await desk.Create(author, novel, ChapterStatuses.Draft, publishAt: Start.AddHours(1));
        await using (var db = database.CreateContext())
        {
            Assert.True(await new NovelsRepository(db).SoftDeleteAsync(novel.Id));
        }

        desk.Clock.Advance(TimeSpan.FromHours(2));
        await desk.PublishDue();

        Assert.Equal(ChapterStatuses.Draft, (await desk.Stored(chapter)).Status);
        Assert.Equal(0, await desk.Announced(chapter, expected: 0));
    }

    [Fact]
    public async Task Reading_a_novel_publishes_its_due_chapters_before_the_answer_and_only_its_own()
    {
        var (author, novel) = await desk.SeedNovel();
        var (otherAuthor, other) = await desk.SeedNovel();
        var chapter = await desk.Create(author, novel, ChapterStatuses.Draft, publishAt: Start.AddHours(1));
        var elsewhere = await desk.Create(otherAuthor, other, ChapterStatuses.Draft, publishAt: Start.AddHours(1));

        // The host stopped the app while they fell due; the first request reads the novel's chapters.
        desk.Clock.Advance(TimeSpan.FromHours(3));
        var answered = false;
        await desk.ReadChapters(novel.Id, async () =>
        {
            Assert.Equal(ChapterStatuses.Published, (await desk.Stored(chapter)).Status); // what the handler then reads
            answered = true;
        });

        Assert.True(answered);
        Assert.Equal(desk.Clock.UtcNow, (await desk.Stored(chapter)).PublishedAt);
        Assert.Equal(1, await desk.Announced(chapter));
        Assert.Equal(1, desk.WindowExtensions(novel.Id));
        Assert.Equal(ChapterStatuses.Draft, (await desk.Stored(elsewhere)).Status); // its novel wasn't read

        // Read again: nothing more.
        await desk.ReadChapters(novel.Id, () => Task.CompletedTask);
        Assert.Equal(1, await desk.Announced(chapter));

        // The other novel's page, by slug, publishes its own.
        await desk.ReadNovelPage(other.Slug, async () => Assert.Equal(ChapterStatuses.Published, (await desk.Stored(elsewhere)).Status));
        Assert.Equal(1, await desk.Announced(elsewhere));
    }

    [Fact]
    public async Task A_read_is_answered_when_publishing_its_due_chapters_fails_and_the_failure_is_logged()
    {
        var (author, novel) = await desk.SeedNovel();
        var chapter = await desk.Create(author, novel, ChapterStatuses.Draft, publishAt: Start.AddHours(1));
        desk.Clock.Advance(TimeSpan.FromHours(1));
        desk.ScheduleCommands.Before(sql => sql.Contains("UPDATE [Chapters] SET"), () => throw new TimeoutException("the database is busy"));
        var log = new ListLogger<ScheduledChapterPublisher>();

        var answered = false;
        await desk.ReadChapters(novel.Id, () =>
        {
            answered = true;
            return Task.CompletedTask;
        }, log);

        Assert.True(answered);
        var error = Assert.Single(log.Entries, e => e.Level == LogLevel.Error);
        var cause = error.Exception;
        while (cause is not null and not TimeoutException)
        {
            cause = cause.InnerException; // EF wraps it: a failed save, a transient failure
        }
        Assert.IsType<TimeoutException>(cause);
        Assert.Equal(ChapterStatuses.Draft, (await desk.Stored(chapter)).Status);

        // The scheduler's next run publishes it.
        await desk.PublishDue();
        Assert.Equal(ChapterStatuses.Published, (await desk.Stored(chapter)).Status);
        Assert.Equal(1, await desk.Announced(chapter));
    }

    [Fact]
    public async Task The_scheduler_publishes_due_chapters_as_soon_as_the_app_starts()
    {
        var (author, novel) = await desk.SeedNovel();
        var chapter = await desk.Create(author, novel, ChapterStatuses.Draft, publishAt: Start.AddHours(1));
        desk.Clock.Advance(TimeSpan.FromHours(1));
        var scheduler = desk.Scheduler();

        await scheduler.StartAsync(CancellationToken.None);
        // Its first run starts at once; up to 30 s on a busy machine.
        for (var waited = 0; waited < 300 && (await desk.Stored(chapter)).Status != ChapterStatuses.Published; waited++)
        {
            await Task.Delay(100);
        }
        await scheduler.StopAsync(CancellationToken.None);

        Assert.Equal(ChapterStatuses.Published, (await desk.Stored(chapter)).Status);
        Assert.Equal(1, await desk.Announced(chapter));
    }

    /// <summary>The command taking this chapter's text lock (ChapterParagraphsRepository.BeginEditAsync).</summary>
    private static bool TakesTheLockOf(DbCommand command, Guid chapterId) =>
        command.CommandText.Contains("sp_getapplock")
        && command.Parameters.Cast<DbParameter>().Any(p => p.ParameterName == "@resource" && Equals(p.Value, $"chapter-text:{chapterId:N}"));

    /// <summary>The task is waiting (for the chapter another save or run holds): not done after half a second.</summary>
    private static async Task AssertWaits(Task task)
    {
        await Task.Delay(500);
        Assert.False(task.IsCompleted, "It should wait for the chapter while another save or run holds it.");
    }

    private async Task<string> Text(Guid chapterId)
    {
        await using var db = database.CreateContext();
        return string.Join("\n", await db.ChapterParagraphs.Where(p => p.ChapterId == chapterId).OrderBy(p => p.OrderIndex)
            .Select(p => p.Content).ToListAsync());
    }
}
