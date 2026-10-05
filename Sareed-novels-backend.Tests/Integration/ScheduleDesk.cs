using System.Data.Common;
using Application.Chapters.Commands.CreateChapter;
using Application.Chapters.Commands.UpdateChapter;
using Application.Chapters.DTOS;
using Application.Chapters.Queries.GetChaptersReader;
using Application.Chapters.Scheduling;
using Application.Novels.DTOS;
using Application.Novels.Queries.GetNovel;
using Application.Services;
using Application.Users;
using Application.Users.Commands.FollowUser;
using AutoMapper;
using Domain.Entities;
using Domain.Repositories;
using Infrastructure.BackgroundJobs;
using Infrastructure.Persistence;
using Infrastructure.Repositories;
using Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Sareed_novels_backend.Tests.Integration;

/// <summary>
/// The author's editor and the schedule (#77) on one clock the test moves: POST and PATCH a chapter through the real
/// handlers (<see cref="CreateChapterCommandHandler"/>, <see cref="UpdateChapterCommandHandler"/>), each on a context of
/// its own as each request has, and <see cref="ScheduledChapterPublisher"/> as the app wires it (the real repositories,
/// a scope per run and per chapter). What a publish tells is recorded: the privilege window (<see cref="Privileges"/>)
/// and readers (<see cref="Notifications"/>; every novel has one reader with it in her library), whom a publish tells
/// in the background (<see cref="Announced"/>).
/// </summary>
internal sealed class ScheduleDesk : IAsyncDisposable
{
    private static readonly IMapper Mapper =
        new MapperConfiguration(cfg => cfg.AddMaps(typeof(ChapterProfiles).Assembly)).CreateMapper();

    private readonly SqlServerDatabase database;
    private readonly ServiceProvider services;

    public ScheduleDesk(SqlServerDatabase database, DateTime start)
    {
        this.database = database;
        Clock = new MutableClock(start);
        var library = Substitute.For<ILibraryRepository>();
        library.GetUsersWithNovelInLibrary(Arg.Any<Guid>()).Returns(new List<string> { "reader" });
        services = new ServiceCollection()
            .AddLogging()
            .AddDbContext<ApplicationDbContext>(o => o.UseSqlServer(database.ConnectionString).AddInterceptors(ScheduleCommands))
            .AddScoped<IChaptersRepository, ChaptersRepository>()
            .AddScoped<IChapterParagraphsRepository, ChapterParagraphsRepository>()
            .AddScoped<INovelsRepository, NovelsRepository>()
            .AddScoped<IChapterSequenceService, ChapterSequenceService>()
            .AddScoped<ScheduledChapterPublisher>()
            .AddSingleton<TimeProvider>(Clock)
            .AddSingleton(Privileges)
            .AddSingleton(Notifications)
            .AddSingleton(library)
            .BuildServiceProvider();
    }

    public MutableClock Clock { get; }

    public IPrivilegeService Privileges { get; } = Substitute.For<IPrivilegeService>();

    public INotificationService Notifications { get; } = Substitute.For<INotificationService>();

    /// <summary>The schedule's own commands (its contexts), to run something just before one of them.</summary>
    public CommandHook ScheduleCommands { get; } = new();

    /// <summary>The scheduler as the app runs it, on this desk's services.</summary>
    public ScheduledChapterPublishingService Scheduler() => new(
        services.GetRequiredService<IServiceScopeFactory>(), Clock, NullLogger<ScheduledChapterPublishingService>.Instance);

    /// <summary>An author and a novel of hers (hidden from readers with <paramref name="hidden"/>), created 30 days before the clock's start.</summary>
    public async Task<(User Author, Novel Novel)> SeedNovel(bool hidden = false)
    {
        await using var db = database.CreateContext();
        var author = Seed.User();
        var novel = Seed.Novel(author, "رواية " + Seed.Marker(), isDraft: hidden, createdAt: Clock.UtcNow.AddDays(-30));
        db.Users.Add(author);
        db.Novels.Add(novel);
        await db.SaveChangesAsync();
        return (author, novel);
    }

    /// <summary>POST /api/novel/{novelId}/chapter as the author, now.</summary>
    public async Task<Guid> Create(User author, Novel novel, string status, DateTime? publishAt = null, string content = "<p>نص الفصل</p>")
    {
        await using var db = database.CreateContext();
        var (chapters, novels) = (new ChaptersRepository(db), new NovelsRepository(db));
        var handler = new CreateChapterCommandHandler(
            NullLogger<CreateChapterCommandHandler>.Instance, chapters, SignedIn(author), novels, Mapper,
            new ChapterSequenceService(NullLogger<ChapterSequenceService>.Instance, novels, chapters), services, Clock);

        var created = await handler.Handle(
            new CreateChapterCommand(novel.Id, status, "فصل " + Seed.Marker(), content) { PublishAt = publishAt }, CancellationToken.None);
        return created.Id;
    }

    /// <summary>
    /// PATCH /api/novel/{novelId}/chapter/{chapterId} as the author, now: the title and text as the editor sends them
    /// (<paramref name="title"/> and <paramref name="content"/>, both null for a save of the status or the schedule
    /// alone), <paramref name="status"/> (null leaves it), and, with <paramref name="setsSchedule"/>, <c>publishAt</c>
    /// (<paramref name="publishAt"/>, null cancelling). The request's context has <paramref name="interceptors"/>.
    /// </summary>
    public async Task<UpdateChapterResult> Save(User author, Novel novel, Guid chapterId, string? status, bool setsSchedule = false,
        DateTime? publishAt = null, string? content = "<p>نص الفصل</p>", string? title = "فصل", int? baseRevision = null,
        params IInterceptor[] interceptors)
    {
        await using var db = database.CreateContext(interceptors);
        var (chapters, novels) = (new ChaptersRepository(db), new NovelsRepository(db));
        var handler = new UpdateChapterCommandHandler(
            NullLogger<UpdateChapterCommandHandler>.Instance, chapters, new ChapterParagraphsRepository(db), novels,
            SignedIn(author), Mapper,
            new ChapterSequenceService(NullLogger<ChapterSequenceService>.Instance, novels, chapters), services, Clock);

        return await handler.Handle(
            new UpdateChapterCommand(chapterId, novel.Id, title, status, content)
            {
                SetsSchedule = setsSchedule, PublishAt = publishAt, BaseRevision = baseRevision
            },
            CancellationToken.None);
    }

    /// <summary>One run of the scheduler: every due chapter. How many it published.</summary>
    public async Task<int> PublishDue()
    {
        await using var scope = services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<ScheduledChapterPublisher>().PublishDueAsync(CancellationToken.None);
    }

    /// <summary>
    /// A request reading a novel's chapter list, through the app's pipeline step before its handler
    /// (<see cref="PublishDueChaptersBehavior{TRequest,TResponse}"/>): <paramref name="handler"/> stands for the handler.
    /// </summary>
    public Task ReadChapters(Guid novelId, Func<Task> handler, ILogger<ScheduledChapterPublisher>? logger = null) =>
        Read(new GetChaptersReaderQuery(novelId), Enumerable.Empty<ChaptersDTO>(), handler, logger);

    /// <summary>A request reading a novel's page by slug, as <see cref="ReadChapters"/>.</summary>
    public Task ReadNovelPage(string slug, Func<Task> handler) => Read(new GetNovelQuery(slug), new NovelsDTO(), handler, null);

    private async Task Read<TRequest, TResponse>(TRequest request, TResponse answer, Func<Task> handler,
        ILogger<ScheduledChapterPublisher>? logger) where TRequest : notnull
    {
        var behavior = new PublishDueChaptersBehavior<TRequest, TResponse>(
            services.GetRequiredService<IServiceScopeFactory>(), logger ?? NullLogger<ScheduledChapterPublisher>.Instance);
        await behavior.Handle(request, async _ =>
        {
            await handler();
            return answer;
        }, CancellationToken.None);
    }

    /// <summary>
    /// How many times readers were told that this chapter came out. A publish sends the notifications in the background
    /// after it reads the novel, so this waits for <paramref name="expected"/> of them (up to 10 s), then a moment more,
    /// so that one more would show.
    /// </summary>
    public async Task<int> Announced(Guid chapterId, int expected = 1)
    {
        for (var waited = 0; waited < 100 && TimesAnnounced(chapterId) < expected; waited++)
        {
            await Task.Delay(100);
        }
        await Task.Delay(200);
        return TimesAnnounced(chapterId);
    }

    private int TimesAnnounced(Guid chapterId) => Notifications.ReceivedCalls().Count(call =>
        call.GetMethodInfo().Name == nameof(INotificationService.SendNewChapterInLibraryNotification)
        && ((Chapter)call.GetArguments()[2]!).Id == chapterId);

    /// <summary>How many times a publish extended the novel's privilege window.</summary>
    public int WindowExtensions(Guid novelId) => Privileges.ReceivedCalls().Count(call =>
        call.GetMethodInfo().Name == nameof(IPrivilegeService.OnChapterPublishedAsync) && (Guid)call.GetArguments()[0]! == novelId);

    public async Task<Chapter> Stored(Guid chapterId)
    {
        await using var db = database.CreateContext();
        return await db.Chapters.AsNoTracking().SingleAsync(c => c.Id == chapterId);
    }

    public async Task<Novel> StoredNovel(Guid novelId)
    {
        await using var db = database.CreateContext();
        return await db.Novels.AsNoTracking().IgnoreQueryFilters().SingleAsync(n => n.Id == novelId);
    }

    public ValueTask DisposeAsync() => services.DisposeAsync();

    private static IUserContext SignedIn(User user)
    {
        var userContext = Substitute.For<IUserContext>();
        userContext.GetCurrentUser().Returns(new CurrentUser(user.Id, user.Email!, user.UserName!, user.DisplayName));
        return userContext;
    }
}

/// <summary>Runs an action once, just before or just after the first command that matches.</summary>
internal sealed class CommandHook : DbCommandInterceptor
{
    private Func<DbCommand, bool>? matches;
    private Func<Task>? action;
    private bool after;

    /// <summary>Before the first command whose SQL matches.</summary>
    public void Before(Func<string, bool> sqlMatches, Func<Task> run) => Before(command => sqlMatches(command.CommandText), run);

    public void Before(Func<DbCommand, bool> commandMatches, Func<Task> run) => Hook(commandMatches, run, after: false);

    /// <summary>
    /// Holds the context that runs the first matching command, just after the command ran (with
    /// <paramref name="afterIt"/>) or just before, until the returned gate is released.
    /// </summary>
    public Gate Hold(Func<DbCommand, bool> commandMatches, bool afterIt)
    {
        var gate = new Gate();
        Hook(commandMatches, gate.HoldAsync, afterIt);
        return gate;
    }

    /// <summary>
    /// Holds the context that takes this chapter's text lock (ChapterParagraphsRepository.BeginEditAsync, #75), once it
    /// has it: an author's save, or the scheduled publish, holding the chapter.
    /// </summary>
    public Gate HoldWithChapter(Guid chapterId) => Hold(command =>
        command.CommandText.Contains("sp_getapplock")
        && command.Parameters.Cast<DbParameter>().Any(p => p.ParameterName == "@resource" && Equals(p.Value, $"chapter-text:{chapterId:N}")),
        afterIt: true);

    private void Hook(Func<DbCommand, bool> commandMatches, Func<Task> run, bool after)
    {
        matches = commandMatches;
        this.after = after;
        action = run;
    }

    private async Task Run(DbCommand command, bool afterIt)
    {
        if (afterIt == after && matches is { } match && match(command) && Interlocked.Exchange(ref action, null) is { } run)
        {
            await run();
        }
    }

    public override async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData eventData,
        InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
    {
        await Run(command, afterIt: false);
        return result;
    }

    public override async ValueTask<DbDataReader> ReaderExecutedAsync(DbCommand command, CommandExecutedEventData eventData,
        DbDataReader result, CancellationToken cancellationToken = default)
    {
        await Run(command, afterIt: true);
        return result;
    }

    public override async ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(DbCommand command, CommandEventData eventData,
        InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        await Run(command, afterIt: false);
        return result;
    }

    public override async ValueTask<int> NonQueryExecutedAsync(DbCommand command, CommandExecutedEventData eventData, int result,
        CancellationToken cancellationToken = default)
    {
        await Run(command, afterIt: true);
        return result;
    }

    /// <summary>Where a <see cref="Hold"/> stops its command's connection, until released.</summary>
    internal sealed class Gate
    {
        private readonly TaskCompletionSource held = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource released = new(TaskCreationOptions.RunContinuationsAsynchronously);

        /// <summary>Completes when the command is held (up to 30 s, or the test fails).</summary>
        public Task Held => held.Task.WaitAsync(TimeSpan.FromSeconds(30));

        public void Release() => released.TrySetResult();

        internal Task HoldAsync()
        {
            held.TrySetResult();
            return released.Task;
        }
    }
}
