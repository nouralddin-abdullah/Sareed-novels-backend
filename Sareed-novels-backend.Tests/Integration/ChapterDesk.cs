using Application.Chapters.Commands.CreateChapter;
using Application.Chapters.Commands.UpdateChapter;
using Application.Chapters.DTOS;
using Application.Services;
using Application.Users;
using AutoMapper;
using Domain.Entities;
using Domain.Repositories;
using Infrastructure.Repositories;
using Infrastructure.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Sareed_novels_backend.Tests.Integration;

/// <summary>
/// The author's editor, through the real handlers on a clock the test moves: POST /api/novel/{novelId}/chapter
/// (<see cref="CreateChapterCommandHandler"/>) and PATCH .../chapter/{chapterId} (<see cref="UpdateChapterCommandHandler"/>),
/// each on a context of its own, as each request has. The new-chapter notifications they send (one call per chapter that
/// comes out, for every reader of the novel, who also get a push) are recorded by <see cref="Notifications"/>.
/// The handlers send them in the background, which finishes before the handler returns here because every service it
/// uses answers at once, so a test can also tell that none was sent.
/// </summary>
internal sealed class ChapterDesk(SqlServerDatabase database, DateTime start)
{
    private static readonly IMapper Mapper =
        new MapperConfiguration(cfg => cfg.AddMaps(typeof(ChapterProfiles).Assembly)).CreateMapper();

    public MutableClock Clock { get; } = new(start);

    /// <summary>What the handlers told readers; every novel has one reader with it in her library.</summary>
    public INotificationService Notifications { get; } = Substitute.For<INotificationService>();

    /// <summary>An author and a public novel of hers, created 30 days before the clock's start.</summary>
    public async Task<(User Author, Novel Novel)> SeedNovel()
    {
        await using var db = database.CreateContext();
        var author = Seed.User();
        var novel = Seed.Novel(author, "رواية " + Seed.Marker(), createdAt: start.AddDays(-30));
        db.Users.Add(author);
        db.Novels.Add(novel);
        await db.SaveChangesAsync();
        return (author, novel);
    }

    /// <summary>POST /api/novel/{novelId}/chapter as the author, published or as a draft, now.</summary>
    public async Task<Guid> Create(User author, Novel novel, string status)
    {
        await using var db = database.CreateContext();
        var (chapters, novels) = (new ChaptersRepository(db), new NovelsRepository(db));
        var handler = new CreateChapterCommandHandler(
            NullLogger<CreateChapterCommandHandler>.Instance, chapters, SignedIn(author), novels, Mapper,
            new ChapterSequenceService(NullLogger<ChapterSequenceService>.Instance, novels, chapters), Services(), Clock);

        var created = await handler.Handle(
            new CreateChapterCommand(novel.Id, status, "فصل " + Seed.Marker(), "<p>نص الفصل</p>"), CancellationToken.None);
        return created.Id;
    }

    /// <summary>
    /// PATCH /api/novel/{novelId}/chapter/{chapterId} as the author, now: the title and text as the editor sends them with
    /// every save, and <paramref name="status"/> (which the editor also always sends; null leaves it as it is).
    /// </summary>
    public async Task Save(User author, Novel novel, Guid chapterId, string? status, string content = "<p>نص الفصل</p>")
    {
        await using var db = database.CreateContext();
        var (chapters, novels) = (new ChaptersRepository(db), new NovelsRepository(db));
        var handler = new UpdateChapterCommandHandler(
            NullLogger<UpdateChapterCommandHandler>.Instance, chapters, new ChapterParagraphsRepository(db), novels,
            SignedIn(author), Mapper,
            new ChapterSequenceService(NullLogger<ChapterSequenceService>.Instance, novels, chapters), Services(), Clock);

        var result = await handler.Handle(
            new UpdateChapterCommand(chapterId, novel.Id, "فصل", status, content), CancellationToken.None);
        Assert.True(result.Success, result.Message);
    }

    /// <summary>How many times readers were told that this chapter came out.</summary>
    public int TimesAnnounced(Guid chapterId) => Notifications.ReceivedCalls().Count(call =>
        call.GetMethodInfo().Name == nameof(INotificationService.SendNewChapterInLibraryNotification)
        && ((Chapter)call.GetArguments()[2]!).Id == chapterId);

    private static IUserContext SignedIn(User user)
    {
        var userContext = Substitute.For<IUserContext>();
        userContext.GetCurrentUser().Returns(new CurrentUser(user.Id, user.Email!, user.UserName!, user.DisplayName));
        return userContext;
    }

    /// <summary>
    /// What the handlers resolve when a chapter comes out: the privilege window, and for the new-chapter notifications
    /// the novel, its one reader and <see cref="Notifications"/>.
    /// </summary>
    private ServiceProvider Services()
    {
        var novels = Substitute.For<INovelsRepository>();
        novels.GetOne(Arg.Any<Guid>()).Returns(call => new Novel
        {
            Id = call.Arg<Guid>(), AuthorId = "author", Title = "رواية", Slug = "s", Summary = "", CoverImageUrl = ""
        });
        var library = Substitute.For<ILibraryRepository>();
        library.GetUsersWithNovelInLibrary(Arg.Any<Guid>()).Returns(new List<string> { "reader" });
        return new ServiceCollection()
            .AddSingleton(Substitute.For<IPrivilegeService>())
            .AddSingleton(library)
            .AddSingleton(Notifications)
            .AddSingleton(novels)
            .BuildServiceProvider();
    }
}
