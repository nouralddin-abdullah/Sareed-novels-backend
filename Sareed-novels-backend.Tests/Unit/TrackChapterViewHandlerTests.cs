using Application.Chapters.Commands.TrackChapterView;
using Application.Services;
using Application.Users;
using Domain.Entities;
using Domain.Repositories;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace Sareed_novels_backend.Tests.Unit;

/// <summary>POST .../view (#25): counted like a read in the reader, and a failure reaches the app, which sends it again.</summary>
public class TrackChapterViewHandlerTests
{
    private readonly IChaptersRepository chapters = Substitute.For<IChaptersRepository>();
    private readonly INovelsRepository novels = Substitute.For<INovelsRepository>();
    private readonly IUserContext userContext = Substitute.For<IUserContext>();
    private readonly IVisitorContext visitorContext = Substitute.For<IVisitorContext>();
    private readonly IPrivilegeService privileges = Substitute.For<IPrivilegeService>();
    private readonly IViewTrackingService views = Substitute.For<IViewTrackingService>();

    private readonly Novel novel = new() { Id = Guid.NewGuid(), AuthorId = "author-1", Title = "t", Slug = "s", Summary = "", CoverImageUrl = "" };
    private readonly Chapter chapter;

    public TrackChapterViewHandlerTests()
    {
        chapter = new Chapter { Id = Guid.NewGuid(), NovelId = novel.Id, Title = "c", Slug = "c", Status = "Published", ChapterIndex = 1 };
        novels.GetOne(novel.Id).Returns(novel);
        chapters.GetChapterById(chapter.Id).Returns(chapter);
        userContext.GetCurrentUser().Returns(new CurrentUser("reader-1", "e", "u", "d"));
        visitorContext.GetVisitorKey().Returns("u:reader-1");
    }

    private Task<bool> Track() =>
        new TrackChapterViewCommandHandler(chapters, novels, userContext, visitorContext, privileges, views,
                NullLogger<TrackChapterViewCommandHandler>.Instance)
            .Handle(new TrackChapterViewCommand(novel.Id, chapter.Id), CancellationToken.None);

    [Fact]
    public async Task A_readers_view_is_counted_with_their_visitor_key()
    {
        views.TrackChapterView(chapter.Id, novel.Id, "u:reader-1", Arg.Any<CancellationToken>()).Returns(true);

        Assert.True(await Track());
    }

    [Fact]
    public async Task A_failure_to_count_is_not_swallowed()
    {
        views.TrackChapterView(chapter.Id, novel.Id, "u:reader-1", Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("the database is down"));

        await Assert.ThrowsAsync<InvalidOperationException>(Track);
    }

    [Fact]
    public async Task Nothing_is_counted_for_the_author_or_a_locked_chapter()
    {
        userContext.GetCurrentUser().Returns(new CurrentUser(novel.AuthorId, "e", "u", "d"));
        Assert.False(await Track());

        userContext.GetCurrentUser().Returns(new CurrentUser("reader-1", "e", "u", "d"));
        privileges.IsChapterLockedAsync(chapter.Id, "reader-1").Returns(true);
        Assert.False(await Track());

        await views.DidNotReceiveWithAnyArgs().TrackChapterView(default, default, default!, default);
    }
}
