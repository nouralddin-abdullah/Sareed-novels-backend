using System.Data.Common;
using Application.Chapters.Commands.UpdateChapter;
using Application.Chapters.DTOS;
using Application.Chapters.Paragraphs;
using Application.Services;
using Application.Users;
using AutoMapper;
using Domain.Constants;
using Domain.Entities;
using Domain.Seo;
using Infrastructure.Persistence;
using Infrastructure.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace Sareed_novels_backend.Tests.Integration;

/// <summary>
/// Saving an edited chapter, on a copy of a real production chapter (<see cref="ProductionChapter"/>) with synthetic
/// readers: comments stay on every paragraph whose words are unchanged, wherever it moved and however it is
/// formatted; a paragraph whose words changed, or that was deleted, goes with all its comments, their replies, likes
/// and notifications. Every comment counter matches a recount afterwards.
/// </summary>
public class ChapterEditTests(SqlServerDatabase database) : IClassFixture<SqlServerDatabase>
{
    private static readonly IMapper Mapper =
        new MapperConfiguration(cfg => cfg.AddMaps(typeof(ChapterProfiles).Assembly)).CreateMapper();

    private const string Typo = "مجر كتاب";
    private const string TypoFixed = "مجرد كتاب";

    [Fact]
    public async Task Saving_the_chapter_unchanged_keeps_every_paragraph_and_comment()
    {
        var world = await SeedChapter();
        var logger = new ListLogger<UpdateChapterCommandHandler>();

        await world.Save(ProductionChapter.Paragraphs, logger);

        await using var db = database.CreateContext();
        Assert.Equal(world.ParagraphIds, await ParagraphIds(db, world));
        Assert.Equal(world.CommentIds.Order(), await CommentIds(db, world));
        await AssertCountersMatchRecount(db, world);
        Assert.Equal((31, 0, 0, 0, 0, 0), SaveLog(logger));
    }

    [Fact]
    public async Task A_typo_fix_makes_a_new_paragraph_and_deletes_only_its_comments()
    {
        var world = await SeedChapter();
        Assert.Contains(Typo, ProductionChapter.Paragraphs[6]);
        var edited = ProductionChapter.Paragraphs.ToArray();
        edited[6] = edited[6].Replace(Typo, TypoFixed);
        var logger = new ListLogger<UpdateChapterCommandHandler>();

        await world.Save(edited, logger);

        await using var db = database.CreateContext();
        var ids = await ParagraphIds(db, world);
        Assert.NotEqual(world.ParagraphIds[6], ids[6]);
        Assert.Equal(world.ParagraphIds.Where((_, i) => i != 6), ids.Where((_, i) => i != 6));
        var fixedParagraph = await db.ChapterParagraphs.SingleAsync(p => p.Id == ids[6]);
        Assert.Equal(edited[6], fixedParagraph.Content);
        Assert.Equal(0, fixedParagraph.CommentsCount);

        // Paragraph 6 had a visible comment (liked, notified) and one its author had deleted.
        await AssertGone(db, world, world.CommentsOn[6]);
        Assert.Equal(world.CommentIds.Except(world.CommentsOn[6]).Order(), await CommentIds(db, world));
        await AssertCountersMatchRecount(db, world);
        Assert.Equal(world.TotalCommentsBefore - 1, (await Chapter(db, world)).TotalCommentsCount);
        Assert.Equal((30, 0, 1, 1, 2, 1), SaveLog(logger));
        Assert.Equal(LogLevel.Warning, logger.Entries.Single(e => e.Values.ContainsKey("CommentsDeleted")).Level);
    }

    [Fact]
    public async Task Deleting_a_paragraph_deletes_its_comments_with_their_replies_likes_and_notifications()
    {
        var world = await SeedChapter();
        var edited = ProductionChapter.Paragraphs.Where((_, i) => i != 1).ToArray();

        await world.Save(edited);

        await using var db = database.CreateContext();
        Assert.Equal(world.ParagraphIds.Where((_, i) => i != 1), await ParagraphIds(db, world));
        Assert.False(await db.ChapterParagraphs.AnyAsync(p => p.Id == world.ParagraphIds[1]));

        // Paragraph 1: a comment, a reply under it (liked), and an old-style reply stored on the chapter.
        Assert.Equal(3, world.CommentsOn[1].Count);
        await AssertGone(db, world, world.CommentsOn[1]);
        Assert.Equal(world.CommentIds.Except(world.CommentsOn[1]).Order(), await CommentIds(db, world));
        await AssertCountersMatchRecount(db, world);
        Assert.Equal(world.TotalCommentsBefore - 1, (await Chapter(db, world)).TotalCommentsCount);
    }

    [Fact]
    public async Task Deleting_a_paragraph_also_deletes_the_pushes_queued_for_its_comments_notifications()
    {
        var world = await SeedChapter();
        Guid gone, kept;
        await using (var db = database.CreateContext())
        {
            // The author has the app: each notification about a comment queues a push to their phone.
            var device = new UserDevice
            {
                Id = Guid.NewGuid(), UserId = world.Author.Id, Token = "token-" + Guid.NewGuid().ToString("N"),
                Platform = DevicePlatforms.Android, CreatedAt = DateTime.UtcNow, LastSeenAt = DateTime.UtcNow
            };
            db.UserDevices.Add(device);
            await db.SaveChangesAsync();
            var notifications = new NotificationsRepository(db);
            gone = (await notifications.CreateNotification(
                Notification(world.Author, world.Readers[0], NotificationType.LikeOnComment, world.CommentsOn[1][0]))).Id;
            kept = (await notifications.CreateNotification(
                Notification(world.Author, world.Readers[0], NotificationType.LikeOnComment, world.CommentsOn[6][0]))).Id;
            Assert.Equal(2, await db.PushOutbox.CountAsync(o => o.DeviceId == device.Id && (o.NotificationId == gone || o.NotificationId == kept)));
        }

        await world.Save(ProductionChapter.Paragraphs.Where((_, i) => i != 1));

        await using var check = database.CreateContext();
        Assert.False(await check.Notifications.AnyAsync(n => n.Id == gone));
        Assert.False(await check.PushOutbox.AnyAsync(o => o.NotificationId == gone));
        Assert.True(await check.Notifications.AnyAsync(n => n.Id == kept));
        Assert.True(await check.PushOutbox.AnyAsync(o => o.NotificationId == kept));
        await AssertCountersMatchRecount(check, world);
    }

    [Fact]
    public async Task Inserting_paragraphs_keeps_every_id_and_comment()
    {
        var world = await SeedChapter();
        var edited = ProductionChapter.Paragraphs.ToList();
        edited.Insert(31, P + "فقرة أخيرة جديدة.");
        edited.Insert(16, P + "\"أين أنتِ يا نور؟\" همس فادي.");
        edited.Insert(0, P + "تمهيد جديد للفصل.");

        await world.Save(edited);

        await using var db = database.CreateContext();
        var ids = await ParagraphIds(db, world);
        Assert.Equal(34, ids.Count);
        Assert.Equal(world.ParagraphIds, ids.Where((_, i) => i is not (0 or 17 or 33)));
        Assert.Equal(world.CommentIds.Order(), await CommentIds(db, world));
        await AssertCountersMatchRecount(db, world);
        Assert.Equal(world.TotalCommentsBefore, (await Chapter(db, world)).TotalCommentsCount);
    }

    [Fact]
    public async Task Reordering_paragraphs_keeps_ids_and_comments()
    {
        var world = await SeedChapter();
        int[] order = [0, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 17, 18, 19, 20, 21, 22, 23, 24, 25, 26, 28, 27, 29, 30, 1, 16];

        await world.Save(order.Select(i => ProductionChapter.Paragraphs[i]));

        await using var db = database.CreateContext();
        Assert.Equal(order.Select(i => world.ParagraphIds[i]), await ParagraphIds(db, world));
        Assert.Equal(world.CommentIds.Order(), await CommentIds(db, world));
        await AssertCountersMatchRecount(db, world);
        foreach (var index in ProductionChapter.CommentedParagraphs)
        {
            Assert.True((await db.ChapterParagraphs.SingleAsync(p => p.Id == world.ParagraphIds[index])).CommentsCount > 0);
        }
    }

    [Fact]
    public async Task Whitespace_and_formatting_changes_keep_ids_and_comments_and_save_the_new_markup()
    {
        var world = await SeedChapter();
        var edited = ProductionChapter.Paragraphs.ToArray();
        edited[1] = edited[1].Replace("، ", "،&nbsp; ").Replace("الأعمال الأدبية", "الأعمال  الأدبية") + "  ";
        edited[16] = edited[16].Replace("نور", "<strong>نور</strong>");
        edited[23] = edited[23].Replace(P, "<p>");
        edited[28] = edited[28].Replace("عملها وحياتها", "عملها<br>وحياتها");
        edited[13] = edited[13].Replace("<br>", "<br />");

        await world.Save(edited);

        await using var db = database.CreateContext();
        Assert.Equal(world.ParagraphIds, await ParagraphIds(db, world));
        Assert.Equal(world.CommentIds.Order(), await CommentIds(db, world));
        await AssertCountersMatchRecount(db, world);

        var bold = await db.ChapterParagraphs.SingleAsync(p => p.Id == world.ParagraphIds[16]);
        Assert.Contains("<strong>نور</strong>", bold.Content);
        Assert.Equal(ParagraphText.Hash(bold.Content), bold.ContentHash);
        Assert.NotNull(bold.UpdatedAt);
        var untouched = await db.ChapterParagraphs.SingleAsync(p => p.Id == world.ParagraphIds[2]);
        Assert.Equal(ProductionChapter.Paragraphs[2], untouched.Content);
        Assert.Null(untouched.UpdatedAt);
    }

    [Fact]
    public async Task Repeated_paragraphs_keep_their_own_ids_and_comments()
    {
        // The chapter with its paragraph 4 ("-بصرامة") repeated at the end, a comment on each copy.
        var paragraphs = ProductionChapter.Paragraphs.Append(ProductionChapter.Paragraphs[4]).ToArray();
        var world = await SeedChapter(paragraphs, extraCommentsOn: [4, 31]);

        // An unrelated edit: both copies stay as they were.
        var edited = paragraphs.ToArray();
        edited[2] = edited[2].Replace("الثالي", "التالي");
        await world.Save(edited);

        await using (var db = database.CreateContext())
        {
            var ids = await ParagraphIds(db, world);
            Assert.Equal(world.ParagraphIds[4], ids[4]);
            Assert.Equal(world.ParagraphIds[31], ids[31]);
            Assert.Equal(world.CommentIds.Order(), await CommentIds(db, world));
            await AssertCountersMatchRecount(db, world);
        }

        // Deleting the first copy deletes that copy's comment; the second copy keeps its id and comment.
        await world.Save(edited.Where((_, i) => i != 4));

        await using (var db = database.CreateContext())
        {
            var ids = await ParagraphIds(db, world);
            Assert.DoesNotContain(world.ParagraphIds[4], ids);
            Assert.Equal(world.ParagraphIds[31], ids[30]);
            Assert.Equal(world.CommentIds.Except(world.CommentsOn[4]).Order(), await CommentIds(db, world));
            await AssertGone(db, world, world.CommentsOn[4]);
            await AssertCountersMatchRecount(db, world);
        }
    }

    [Fact]
    public async Task A_realistic_edit_session_keeps_what_is_unchanged_and_deletes_the_rest_consistently()
    {
        var world = await SeedChapter();
        var edited = ProductionChapter.Paragraphs.ToList();
        edited[6] = edited[6].Replace(Typo, TypoFixed); // typo fix: comments on 6 go
        edited[16] = edited[16].Replace("نور", "<em>نور</em>"); // italic: comments on 16 stay
        edited[1] = "  " + edited[1] + " "; // spacing: comments on 1 stay
        (edited[27], edited[28]) = (edited[28], edited[27]); // reorder: comments on 28 stay
        edited.RemoveAt(23); // delete: comments on 23 go
        edited.Insert(10, P + "ولم يكن أحد يتوقع ما سيحدث بعد ذلك."); // insert
        var logger = new ListLogger<UpdateChapterCommandHandler>();

        await world.Save(edited, logger);

        await using var db = database.CreateContext();
        var ids = await ParagraphIds(db, world);
        Assert.Equal(31, ids.Count);
        foreach (var unchanged in new[] { 1, 16, 28 })
        {
            Assert.Contains(world.ParagraphIds[unchanged], ids);
        }

        Assert.DoesNotContain(world.ParagraphIds[6], ids);
        Assert.DoesNotContain(world.ParagraphIds[23], ids);
        var gone = world.CommentsOn[6].Concat(world.CommentsOn[23]).ToList();
        await AssertGone(db, world, gone);
        Assert.Equal(world.CommentIds.Except(gone).Order(), await CommentIds(db, world));
        await AssertCountersMatchRecount(db, world);
        Assert.Equal(world.TotalCommentsBefore - 2, (await Chapter(db, world)).TotalCommentsCount);

        var (kept, moved, created, removed, deleted, visible) = SaveLog(logger);
        Assert.Equal((29, 2, 2, 3, 2), (kept + moved, created, removed, deleted, visible));
    }

    [Fact]
    public async Task A_comment_being_saved_when_the_edit_starts_loses_to_the_edit()
    {
        var world = await SeedChapter();

        // A reader's comment on paragraph 23 is inserted but not yet committed, as inside CreateComment.
        await using var readerDb = database.CreateContext();
        await using var readerTransaction = await readerDb.Database.BeginTransactionAsync();
        var late = new Comments { Id = Guid.NewGuid(), UserId = world.Readers[0].Id, ParagraphId = world.ParagraphIds[23], Content = "تعليق متأخر" };
        readerDb.Comments.Add(late);
        await readerDb.SaveChangesAsync();

        // The author deletes paragraph 23; the edit has to wait for the reader's row.
        var editing = world.Save(ProductionChapter.Paragraphs.Where((_, i) => i != 23));
        await Task.Delay(TimeSpan.FromMilliseconds(700));
        Assert.False(editing.IsCompleted);

        // The reader's request goes on to count its comment on the paragraph, which the edit now holds: a deadlock,
        // and the reader's request is the one that fails.
        await Assert.ThrowsAnyAsync<Exception>(() => readerDb.Database.ExecuteSqlRawAsync(
            "UPDATE ChapterParagraphs SET CommentsCount = CommentsCount + 1 WHERE Id = {0}", world.ParagraphIds[23]));
        await editing;

        await using var db = database.CreateContext();
        Assert.False(await db.ChapterParagraphs.AnyAsync(p => p.Id == world.ParagraphIds[23]));
        Assert.False(await db.Comments.IgnoreQueryFilters().AnyAsync(c => c.Id == late.Id));
        await AssertGone(db, world, world.CommentsOn[23]);
        await AssertCountersMatchRecount(db, world);
    }

    [Fact]
    public async Task A_comment_posted_during_the_edit_on_a_paragraph_it_removes_fails_and_the_edit_goes_through()
    {
        var world = await SeedChapter();
        var pause = new PauseBefore("DECLARE @doomed");
        var editDb = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlServer(database.ConnectionString).AddInterceptors(pause).Options);
        var last = ProductionChapter.Paragraphs.Length - 1;

        // The author deletes the last paragraph (so the edit has written little yet): the edit has locked the
        // paragraph and is about to delete its comments.
        var editing = world.Save(ProductionChapter.Paragraphs[..last], context: editDb);
        Assert.True(await pause.Reached.WaitAsync(TimeSpan.FromSeconds(30)));

        // A reader comments on that paragraph right then: their new row waits for the paragraph.
        var late = Guid.NewGuid();
        var commenting = Task.Run(async () =>
        {
            await using var readerDb = database.CreateContext();
            await new CommentsRepository(readerDb).CreateComment(new Comments
            {
                Id = late, UserId = world.Readers[0].Id, ParagraphId = world.ParagraphIds[last], Content = "تعليق متأخر"
            });
        });
        await Task.Delay(TimeSpan.FromMilliseconds(700));
        Assert.False(commenting.IsCompleted);

        // The edit goes on and meets the reader's row: a deadlock, which the reader's request loses.
        pause.Release();
        await editing;
        await Assert.ThrowsAnyAsync<Exception>(() => commenting);

        await using var db = database.CreateContext();
        Assert.Equal(world.ParagraphIds[..last], await ParagraphIds(db, world));
        Assert.False(await db.Comments.IgnoreQueryFilters().AnyAsync(c => c.Id == late));
        Assert.Equal(world.CommentIds.Order(), await CommentIds(db, world));
        await AssertCountersMatchRecount(db, world);
    }

    [Fact]
    public async Task Saving_a_chapter_does_not_write_back_counters_that_changed_meanwhile()
    {
        var world = await SeedChapter();

        await using var editor = database.CreateContext();
        var chapters = new ChaptersRepository(editor);
        var loaded = await chapters.GetChapterById(world.ChapterId);

        // A reader comments on the chapter while the author's save is under way.
        await using (var reader = database.CreateContext())
        {
            await new CommentsRepository(reader).CreateComment(new Comments
            {
                Id = Guid.NewGuid(), UserId = world.Readers[0].Id, ChapterId = world.ChapterId, Content = "تعليق أثناء الحفظ"
            });
        }

        loaded!.Title = "عنوان جديد";
        Assert.True(await chapters.UpdateChapter(loaded));

        await using var db = database.CreateContext();
        var chapter = await Chapter(db, world);
        Assert.Equal("عنوان جديد", chapter.Title);
        Assert.Equal(world.TotalCommentsBefore + 1, chapter.TotalCommentsCount);
        await AssertCountersMatchRecount(db, world);
    }

    // ---- The seeded chapter ----

    private const string P = "<p class=\"min-h-[1em]\">";

    private sealed class World(SqlServerDatabase database)
    {
        public required User Author { get; init; }
        public required User[] Readers { get; init; }
        public required Guid NovelId { get; init; }
        public required Guid ChapterId { get; init; }
        public required string Title { get; init; }
        public required List<Guid> ParagraphIds { get; init; }
        public List<Guid> CommentIds { get; } = [];
        public Dictionary<int, List<Guid>> CommentsOn { get; } = [];
        public int TotalCommentsBefore { get; set; }

        public IEnumerable<string> UserIds => Readers.Select(r => r.Id).Append(Author.Id);

        /// <summary>Saves the chapter as the web editor does: its HTML, every paragraph closed.</summary>
        public async Task Save(
            IEnumerable<string> paragraphs, ILogger<UpdateChapterCommandHandler>? logger = null, ApplicationDbContext? context = null)
        {
            await using var db = context ?? database.CreateContext();
            var userContext = Substitute.For<IUserContext>();
            userContext.GetCurrentUser().Returns(new CurrentUser(Author.Id, "author@test.local", Author.UserName!, Author.DisplayName));
            var handler = new UpdateChapterCommandHandler(
                logger ?? new ListLogger<UpdateChapterCommandHandler>(),
                new ChaptersRepository(db), new ChapterParagraphsRepository(db), new NovelsRepository(db), userContext,
                Mapper, Substitute.For<IChapterSequenceService>(), Substitute.For<IServiceProvider>());

            var content = string.Concat(paragraphs.Select(p => p + "</p>"));
            var result = await handler.Handle(
                new UpdateChapterCommand(ChapterId, NovelId, Title, "Published", content), CancellationToken.None);

            Assert.True(result.Success, result.Message);
        }
    }

    /// <summary>
    /// The chapter with comments shaped like production's (one on each of <see cref="ProductionChapter.CommentedParagraphs"/>,
    /// a reply under the one on paragraph 1, a chapter comment with a reply), plus what makes deletion hard: a second
    /// comment on paragraph 6 that its author deleted, likes, notifications, and an old-style reply to the paragraph 1
    /// comment stored on the chapter.
    /// </summary>
    private async Task<World> SeedChapter(string[]? paragraphs = null, int[]? extraCommentsOn = null)
    {
        paragraphs ??= ProductionChapter.Paragraphs;
        var author = Seed.User("مشعل");
        var readers = Enumerable.Range(1, 4).Select(i => Seed.User($"قارئ {i}")).ToArray();
        var novel = Seed.Novel(author, "مذكرات " + Seed.Marker());
        var chapterId = Guid.NewGuid();
        const string title = "الفصل الخامس: صدفة تضيء الظلام";
        var createdAt = DateTime.UtcNow.AddDays(-280);

        await using var db = database.CreateContext();
        db.Users.AddRange(readers.Append(author));
        db.Novels.Add(novel);
        db.Chapters.Add(new Chapter
        {
            Id = chapterId, NovelId = novel.Id, Title = title, Slug = Slugs.For(chapterId, title), Status = "Published",
            ChapterIndex = 5, ParagraphsCount = paragraphs.Length, CreatedAt = createdAt
        });
        var saved = paragraphs.Select((content, i) => new ChapterParagraph
        {
            Id = Guid.NewGuid(), ChapterId = chapterId, Content = content, ContentHash = ParagraphText.Hash(content),
            OrderIndex = i, ContentType = "text", CreatedAt = createdAt
        }).ToList();
        db.ChapterParagraphs.AddRange(saved);
        await db.SaveChangesAsync();

        var world = new World(database)
        {
            Author = author, Readers = readers, NovelId = novel.Id, ChapterId = chapterId, Title = title,
            ParagraphIds = saved.Select(p => p.Id).ToList()
        };

        var comments = new CommentsRepository(db);
        var likes = new CommentLikesRepository(db);
        var at = createdAt.AddDays(1);

        async Task<Guid> Comment(User user, int? paragraph, Guid? parent = null, bool onChapter = false)
        {
            var comment = new Comments
            {
                Id = Guid.NewGuid(), UserId = user.Id, Content = $"تعليق {world.CommentIds.Count + 1}",
                ParagraphId = paragraph is { } p ? saved[p].Id : null, ChapterId = onChapter ? chapterId : null,
                ParentCommentId = parent, CreatedAt = at = at.AddMinutes(1)
            };
            await comments.CreateComment(comment);
            world.CommentIds.Add(comment.Id);
            if (paragraph is { } index)
            {
                (world.CommentsOn.TryGetValue(index, out var list) ? list : world.CommentsOn[index] = []).Add(comment.Id);
            }

            // The notification the comment sent (for a reply, the parent's author gets it; who doesn't matter here).
            db.Notifications.Add(Notification(author, user,
                parent == null ? NotificationType.CommentOnChapter : NotificationType.ReplyToComment, comment.Id));
            return comment.Id;
        }

        foreach (var index in ProductionChapter.CommentedParagraphs.Concat(extraCommentsOn ?? []))
        {
            await Comment(readers[index % readers.Length], index);
        }

        var onParagraph1 = world.CommentsOn[1][0];
        var reply = await Comment(readers[2], 1, parent: onParagraph1);
        var oldStyleReply = await Comment(readers[3], paragraph: null, parent: onParagraph1, onChapter: true);
        world.CommentsOn[1].Add(oldStyleReply);

        var onChapter = await Comment(readers[1], paragraph: null, onChapter: true);
        await Comment(readers[0], paragraph: null, parent: onChapter, onChapter: true);

        var deletedByAuthor = await Comment(readers[3], 6);
        Assert.True(await comments.DeleteComment(deletedByAuthor));

        var onParagraph6 = world.CommentsOn[6][0];
        await likes.LikeComment(readers[0].Id, onParagraph6);
        await likes.LikeComment(readers[1].Id, reply);
        await likes.LikeComment(readers[2].Id, onChapter);
        db.Notifications.Add(Notification(readers[1], readers[0], NotificationType.LikeOnComment, onParagraph6));
        await db.SaveChangesAsync();

        world.TotalCommentsBefore = (await Chapter(db, world)).TotalCommentsCount;
        Assert.Equal(1 + ProductionChapter.CommentedParagraphs.Length + (extraCommentsOn?.Length ?? 0), world.TotalCommentsBefore);
        await AssertCountersMatchRecount(db, world);
        return world;
    }

    private static Notification Notification(User to, User from, string type, Guid commentId) => new()
    {
        Id = Guid.NewGuid(), UserId = to.Id, Type = type, ActorId = from.Id, ActorDisplayName = from.DisplayName,
        Message = "إشعار", ActionUrl = "/novel/x/chapter/y", RelatedEntityId = commentId, RelatedEntityType = "Comment",
        CreatedAt = DateTime.UtcNow
    };

    // ---- Checks ----

    private static Task<List<Guid>> ParagraphIds(ApplicationDbContext db, World world) =>
        db.ChapterParagraphs.Where(p => p.ChapterId == world.ChapterId).OrderBy(p => p.OrderIndex).Select(p => p.Id).ToListAsync();

    /// <summary>Every comment row of this world's users, deleted by their authors or not.</summary>
    private static async Task<List<Guid>> CommentIds(ApplicationDbContext db, World world)
    {
        var users = world.UserIds.ToList();
        return (await db.Comments.IgnoreQueryFilters().Where(c => users.Contains(c.UserId)).Select(c => c.Id).ToListAsync())
            .Order().ToList();
    }

    private static Task<Chapter> Chapter(ApplicationDbContext db, World world) =>
        db.Chapters.AsNoTracking().SingleAsync(c => c.Id == world.ChapterId);

    /// <summary>The comments, and everything that pointed at them, are gone.</summary>
    private static async Task AssertGone(ApplicationDbContext db, World world, IReadOnlyCollection<Guid> comments)
    {
        Assert.NotEmpty(comments);
        var ids = comments.ToList();
        Assert.False(await db.Comments.IgnoreQueryFilters().AnyAsync(c => ids.Contains(c.Id)));
        Assert.False(await db.Comments.IgnoreQueryFilters().AnyAsync(c => c.ParentCommentId != null && ids.Contains(c.ParentCommentId.Value)));
        Assert.False(await db.CommentLikes.AnyAsync(l => ids.Contains(l.CommentId)));
        Assert.False(await db.Notifications.AnyAsync(n => n.RelatedEntityId != null && ids.Contains(n.RelatedEntityId.Value)));
    }

    /// <summary>
    /// Counters against a recount: a paragraph counts its visible top-level comments, the chapter its own
    /// (CommentsCount) and those plus its paragraphs' (TotalCommentsCount), a user every visible comment they wrote.
    /// </summary>
    private static async Task AssertCountersMatchRecount(ApplicationDbContext db, World world)
    {
        var chapter = await Chapter(db, world);
        var paragraphs = await db.ChapterParagraphs.AsNoTracking().Where(p => p.ChapterId == world.ChapterId).ToListAsync();
        var users = world.UserIds.ToList();
        var comments = await db.Comments.AsNoTracking().Where(c => users.Contains(c.UserId)).ToListAsync(); // visible only

        foreach (var paragraph in paragraphs)
        {
            Assert.Equal(comments.Count(c => c.ParagraphId == paragraph.Id && c.ParentCommentId == null), paragraph.CommentsCount);
        }

        var own = comments.Count(c => c.ChapterId == chapter.Id && c.ParagraphId == null && c.ParentCommentId == null);
        Assert.Equal(own, chapter.CommentsCount);
        Assert.Equal(own + paragraphs.Sum(p => p.CommentsCount), chapter.TotalCommentsCount);
        Assert.Equal(paragraphs.Count, chapter.ParagraphsCount);
        Assert.Equal(Enumerable.Range(0, paragraphs.Count), paragraphs.Select(p => p.OrderIndex).Order());

        foreach (var user in await db.Users.AsNoTracking().Where(u => users.Contains(u.Id)).ToListAsync())
        {
            Assert.Equal(comments.Count(c => c.UserId == user.Id), user.CommentsCount);
        }

        // Nothing points at a comment that is gone.
        Assert.False(await db.Notifications.AnyAsync(n => users.Contains(n.UserId) && n.RelatedEntityType == "Comment"
            && !db.Comments.IgnoreQueryFilters().Any(c => c.Id == n.RelatedEntityId)));
    }

    /// <summary>The handler's summary line: kept, moved, new, removed, comments deleted, visible comments deleted.</summary>
    private static (int, int, int, int, int, int) SaveLog(ListLogger<UpdateChapterCommandHandler> logger)
    {
        var values = logger.Entries.Single(e => e.Values.ContainsKey("CommentsDeleted")).Values;
        return ((int)values["Kept"]!, (int)values["Moved"]!, (int)values["Created"]!, (int)values["Removed"]!,
            (int)values["CommentsDeleted"]!, (int)values["VisibleCommentsDeleted"]!);
    }
}

/// <summary>Holds the first command whose SQL contains <paramref name="marker"/> until <see cref="Release"/>.</summary>
internal sealed class PauseBefore(string marker) : DbCommandInterceptor
{
    private readonly TaskCompletionSource released = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int paused;

    public SemaphoreSlim Reached { get; } = new(0);

    public void Release() => released.TrySetResult();

    public override async ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
        DbCommand command, CommandEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        if (command.CommandText.Contains(marker) && Interlocked.Exchange(ref paused, 1) == 0)
        {
            Reached.Release();
            await released.Task;
        }

        return result;
    }
}
