using Application.Chapters.Commands.CreateChapter;
using Application.Chapters.Commands.UpdateChapter;
using Application.Chapters.DTOS;
using Application.Services;
using Application.Users;
using AutoMapper;
using Domain.Constants;
using Domain.Entities;
using Domain.Repositories;
using Infrastructure.Repositories;
using Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Sareed_novels_backend.Tests.Integration;

/// <summary>
/// #33: every way a chapter becomes published stamps when it came out, on the handlers' clock: created published
/// (CreateChapterCommandHandler) and a draft published later (UpdateChapterCommandHandler). The first publish is
/// kept through unpublishing, publishing again and later saves; drafts that were never published have none.
/// </summary>
public class ChapterPublishingTests(SqlServerDatabase database) : IClassFixture<SqlServerDatabase>
{
    private static readonly IMapper Mapper =
        new MapperConfiguration(cfg => cfg.AddMaps(typeof(ChapterProfiles).Assembly)).CreateMapper();

    private static readonly DateTime Written = new(2026, 9, 1, 8, 0, 0, DateTimeKind.Utc);

    private readonly MutableClock clock = new(Written);

    private sealed record Stored(string Status, DateTime CreatedAt, DateTime? PublishedAt);

    [Fact]
    public async Task A_chapter_created_published_comes_out_when_it_is_created_and_a_draft_does_not()
    {
        var (author, novel) = await SeedNovel();

        var published = await Create(author, novel, ChapterStatuses.Published);
        clock.Advance(TimeSpan.FromMinutes(5));
        var draft = await Create(author, novel, ChapterStatuses.Draft);

        Assert.Equal(new Stored(ChapterStatuses.Published, Written, Written), await Chapter(published));
        Assert.Equal(new Stored(ChapterStatuses.Draft, Written.AddMinutes(5), null), await Chapter(draft));
    }

    [Fact]
    public async Task A_draft_published_later_comes_out_when_it_is_published_not_when_it_was_written()
    {
        var (author, novel) = await SeedNovel();
        var chapter = await Create(author, novel, ChapterStatuses.Draft);

        // Saved as a draft again first (the editor sends the status with every save): still not out.
        clock.Advance(TimeSpan.FromDays(1));
        await Save(author, novel, chapter, ChapterStatuses.Draft);
        Assert.Equal(new Stored(ChapterStatuses.Draft, Written, null), await Chapter(chapter));

        clock.Advance(TimeSpan.FromDays(2));
        await Save(author, novel, chapter, ChapterStatuses.Published);

        Assert.Equal(new Stored(ChapterStatuses.Published, Written, Written.AddDays(3)), await Chapter(chapter));
    }

    [Fact]
    public async Task Unpublishing_and_publishing_again_keep_the_first_publish_time()
    {
        var (author, novel) = await SeedNovel();
        var chapter = await Create(author, novel, ChapterStatuses.Draft);
        clock.Advance(TimeSpan.FromDays(1));
        await Save(author, novel, chapter, ChapterStatuses.Published);
        var firstOut = Written.AddDays(1);

        clock.Advance(TimeSpan.FromDays(1));
        await Save(author, novel, chapter, ChapterStatuses.Draft);
        Assert.Equal(new Stored(ChapterStatuses.Draft, Written, firstOut), await Chapter(chapter));

        clock.Advance(TimeSpan.FromDays(1));
        await Save(author, novel, chapter, ChapterStatuses.Published);
        Assert.Equal(new Stored(ChapterStatuses.Published, Written, firstOut), await Chapter(chapter));

        // Later saves, with the status the editor resends or without one, keep it too.
        clock.Advance(TimeSpan.FromDays(1));
        await Save(author, novel, chapter, ChapterStatuses.Published);
        await Save(author, novel, chapter, status: null);
        Assert.Equal(new Stored(ChapterStatuses.Published, Written, firstOut), await Chapter(chapter));
    }

    [Fact]
    public async Task A_chapter_created_published_keeps_its_creation_time_through_unpublishing_and_publishing_again()
    {
        var (author, novel) = await SeedNovel();
        var chapter = await Create(author, novel, ChapterStatuses.Published);

        clock.Advance(TimeSpan.FromDays(1));
        await Save(author, novel, chapter, ChapterStatuses.Draft);
        clock.Advance(TimeSpan.FromDays(1));
        await Save(author, novel, chapter, ChapterStatuses.Published);

        Assert.Equal(new Stored(ChapterStatuses.Published, Written, Written), await Chapter(chapter));
    }

    private async Task<(User Author, Novel Novel)> SeedNovel()
    {
        await using var db = database.CreateContext();
        var author = Seed.User();
        var novel = Seed.Novel(author, "رواية " + Seed.Marker(), createdAt: Written.AddDays(-30));
        db.Users.Add(author);
        db.Novels.Add(novel);
        await db.SaveChangesAsync();
        return (author, novel);
    }

    /// <summary>POST /api/novel/{novelId}/chapter as the author, through its handler.</summary>
    private async Task<Guid> Create(User author, Novel novel, string status)
    {
        await using var db = database.CreateContext();
        var (chapters, novels) = (new ChaptersRepository(db), new NovelsRepository(db));
        var handler = new CreateChapterCommandHandler(
            NullLogger<CreateChapterCommandHandler>.Instance, chapters, SignedIn(author), novels, Mapper,
            new ChapterSequenceService(NullLogger<ChapterSequenceService>.Instance, novels, chapters), Services(), clock);

        var created = await handler.Handle(
            new CreateChapterCommand(novel.Id, status, "فصل " + Seed.Marker(), "<p>نص الفصل</p>"), CancellationToken.None);
        return created.Id;
    }

    /// <summary>PATCH /api/novel/{novelId}/chapter/{chapterId} as the author, through its handler.</summary>
    private async Task Save(User author, Novel novel, Guid chapterId, string? status)
    {
        await using var db = database.CreateContext();
        var (chapters, novels) = (new ChaptersRepository(db), new NovelsRepository(db));
        var handler = new UpdateChapterCommandHandler(
            NullLogger<UpdateChapterCommandHandler>.Instance, chapters, new ChapterParagraphsRepository(db), novels,
            SignedIn(author), Mapper,
            new ChapterSequenceService(NullLogger<ChapterSequenceService>.Instance, novels, chapters), Services(), clock);

        var result = await handler.Handle(
            new UpdateChapterCommand(chapterId, novel.Id, "فصل", status, "<p>نص الفصل</p>"), CancellationToken.None);
        Assert.True(result.Success, result.Message);
    }

    private async Task<Stored> Chapter(Guid chapterId)
    {
        await using var db = database.CreateContext();
        return await db.Chapters
            .Where(c => c.Id == chapterId)
            .Select(c => new Stored(c.Status, c.CreatedAt, c.PublishedAt))
            .SingleAsync();
    }

    private static IUserContext SignedIn(User user)
    {
        var userContext = Substitute.For<IUserContext>();
        userContext.GetCurrentUser().Returns(new CurrentUser(user.Id, user.Email!, user.UserName!, user.DisplayName));
        return userContext;
    }

    /// <summary>
    /// What the handlers resolve when a chapter comes out: the privilege window, and the new-chapter notifications,
    /// which go to nobody here (no one has the novel in their library).
    /// </summary>
    private static ServiceProvider Services()
    {
        var library = Substitute.For<ILibraryRepository>();
        library.GetUsersWithNovelInLibrary(Arg.Any<Guid>()).Returns(new List<string>());
        return new ServiceCollection()
            .AddSingleton(Substitute.For<IPrivilegeService>())
            .AddSingleton(library)
            .AddSingleton(Substitute.For<INotificationService>())
            .AddSingleton(Substitute.For<INovelsRepository>())
            .BuildServiceProvider();
    }
}
