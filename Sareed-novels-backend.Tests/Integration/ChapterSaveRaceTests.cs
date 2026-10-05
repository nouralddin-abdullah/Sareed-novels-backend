using Application.Chapters.Commands.UpdateChapter;
using Application.Chapters.DTOS;
using Application.Chapters.Paragraphs;
using Application.Services;
using Application.Users;
using AutoMapper;
using Domain.Constants;
using Domain.Entities;
using Domain.Exceptions;
using Infrastructure.Persistence;
using Infrastructure.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Sareed_novels_backend.Tests.Integration;

/// <summary>
/// #75: the revision check and the save are one step. Two saves from the same revision, the second arriving while the
/// first is still writing: the second waits for it, then finds the revision moved and is refused, with nothing of it
/// saved. And the revision is only ever written over the one the copy was loaded at.
/// </summary>
public class ChapterSaveRaceTests(SqlServerDatabase database) : IClassFixture<SqlServerDatabase>
{
    private static readonly IMapper Mapper =
        new MapperConfiguration(cfg => cfg.AddMaps(typeof(ChapterProfiles).Assembly)).CreateMapper();

    [Fact]
    public async Task A_save_from_the_revision_a_save_under_way_started_from_waits_for_it_and_is_refused()
    {
        var (author, novel, chapterId) = await SeedChapter();

        // The phone's save has checked revision 1 and written the paragraphs; it stops before the chapter's row.
        var pause = new PauseBefore("[ParagraphsCount]");
        await using var phoneDb = database.CreateContext(pause);
        var phone = Save(phoneDb, author, novel, chapterId, "<p>من الهاتف</p>", baseRevision: 1);
        Assert.True(await pause.Reached.WaitAsync(TimeSpan.FromSeconds(30)));

        // The web saves from revision 1 too: it waits for the phone's save.
        await using var webDb = database.CreateContext();
        var web = Save(webDb, author, novel, chapterId, "<p>من الموقع</p><p>فقرة أخرى</p>", baseRevision: 1);
        await Task.Delay(TimeSpan.FromMilliseconds(700));
        Assert.False(web.IsCompleted);

        pause.Release();
        Assert.Equal(2, (await phone).Revision);
        var refused = await Assert.ThrowsAsync<ChapterChangedException>(() => web);
        Assert.Equal(2, refused.Revision);

        await using var db = database.CreateContext();
        var chapter = await db.Chapters.AsNoTracking().SingleAsync(c => c.Id == chapterId);
        var paragraphs = await db.ChapterParagraphs.AsNoTracking().Where(p => p.ChapterId == chapterId).Select(p => p.Content).ToListAsync();
        Assert.Equal((2, 1), (chapter.Revision, chapter.ParagraphsCount));
        Assert.Equal(["من الهاتف"], paragraphs);
    }

    [Fact]
    public async Task Without_a_base_revision_the_second_save_goes_after_the_first_as_before()
    {
        var (author, novel, chapterId) = await SeedChapter();

        var pause = new PauseBefore("[ParagraphsCount]");
        await using var phoneDb = database.CreateContext(pause);
        var phone = Save(phoneDb, author, novel, chapterId, "<p>من الهاتف</p>", baseRevision: null);
        Assert.True(await pause.Reached.WaitAsync(TimeSpan.FromSeconds(30)));

        await using var webDb = database.CreateContext();
        var web = Save(webDb, author, novel, chapterId, "<p>من الموقع</p>", baseRevision: null);
        await Task.Delay(TimeSpan.FromMilliseconds(500));
        Assert.False(web.IsCompleted);

        pause.Release();
        Assert.Equal(2, (await phone).Revision);
        Assert.Equal(3, (await web).Revision);

        await using var db = database.CreateContext();
        Assert.Equal(["من الموقع"], await db.ChapterParagraphs.Where(p => p.ChapterId == chapterId).Select(p => p.Content).ToListAsync());
    }

    [Fact]
    public async Task The_revision_is_written_only_over_the_one_the_copy_was_loaded_at()
    {
        var (_, _, chapterId) = await SeedChapter();
        await using var firstDb = database.CreateContext();
        await using var secondDb = database.CreateContext();
        var first = (await new ChaptersRepository(firstDb).GetChapterById(chapterId))!;
        var second = (await new ChaptersRepository(secondDb).GetChapterById(chapterId))!;

        first.Title = "عنوان أول";
        first.Revision++;
        Assert.True((await new ChaptersRepository(firstDb).UpdateChapter(first)).Saved);

        // A copy loaded at revision 1 can't move it again: nothing of that save is stored.
        second.Title = "عنوان ثانٍ";
        second.Revision++;
        var refused = await Assert.ThrowsAsync<ChapterChangedException>(() => new ChaptersRepository(secondDb).UpdateChapter(second));
        Assert.Equal(2, refused.Revision);

        await using var db = database.CreateContext();
        var stored = await db.Chapters.AsNoTracking().SingleAsync(c => c.Id == chapterId);
        Assert.Equal(("عنوان أول", 2), (stored.Title, stored.Revision));
    }

    private static Task<UpdateChapterResult> Save(
        ApplicationDbContext db, User author, Novel novel, Guid chapterId, string content, int? baseRevision)
    {
        var userContext = Substitute.For<IUserContext>();
        userContext.GetCurrentUser().Returns(new CurrentUser(author.Id, author.Email!, author.UserName!, author.DisplayName));
        var handler = new UpdateChapterCommandHandler(
            NullLogger<UpdateChapterCommandHandler>.Instance, new ChaptersRepository(db), new ChapterParagraphsRepository(db),
            new NovelsRepository(db), userContext, Mapper, Substitute.For<IChapterSequenceService>(),
            Substitute.For<IServiceProvider>(), TimeProvider.System);

        return Task.Run(() => handler.Handle(
            new UpdateChapterCommand(chapterId, novel.Id, "فصل", null, content) { BaseRevision = baseRevision },
            CancellationToken.None));
    }

    /// <summary>A published chapter at revision 1, with one paragraph.</summary>
    private async Task<(User Author, Novel Novel, Guid ChapterId)> SeedChapter()
    {
        var author = Seed.User();
        var novel = Seed.Novel(author, "رواية " + Seed.Marker());
        var chapter = Seed.Chapters(novel, 1, DateTime.UtcNow.AddDays(-1)).Single();
        chapter.Title = "فصل";
        chapter.ParagraphsCount = 1;

        await using var db = database.CreateContext();
        db.Users.Add(author);
        db.Novels.Add(novel);
        db.Chapters.Add(chapter);
        db.ChapterParagraphs.Add(ParagraphRows.New(chapter.Id, new FormattedParagraph(ParagraphKinds.Text, "نص", null), 0, DateTime.UtcNow));
        await db.SaveChangesAsync();
        Assert.Equal(1, chapter.Revision);
        return (author, novel, chapter.Id);
    }
}
