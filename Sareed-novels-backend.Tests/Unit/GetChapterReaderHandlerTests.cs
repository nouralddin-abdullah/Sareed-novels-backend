using Application.Chapters.DTOS;
using Application.Chapters.Queries.GetChapterReader;
using Application.Services;
using Application.Users;
using AutoMapper;
using Domain.Entities;
using Domain.Exceptions;
using Domain.Repositories;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Sareed_novels_backend.Tests.Unit;

public class GetChapterReaderHandlerTests
{
    private readonly IChaptersRepository chapters = Substitute.For<IChaptersRepository>();
    private readonly IChapterParagraphsRepository paragraphs = Substitute.For<IChapterParagraphsRepository>();
    private readonly INovelsRepository novels = Substitute.For<INovelsRepository>();
    private readonly IMapper mapper = Substitute.For<IMapper>();
    private readonly IUserContext userContext = Substitute.For<IUserContext>();
    private readonly IVisitorContext visitorContext = Substitute.For<IVisitorContext>();
    private readonly IPrivilegeService privileges = Substitute.For<IPrivilegeService>();

    private readonly Novel novel = new() { Id = Guid.NewGuid(), AuthorId = "author-1", Title = "t", Slug = "s", Summary = "", CoverImageUrl = "" };

    private GetChapterReaderHandler Handler() => new(
        chapters, paragraphs, novels, mapper, userContext, visitorContext, privileges,
        Substitute.For<IServiceScopeFactory>(), NullLogger<GetChapterReaderHandler>.Instance);

    private Chapter ChapterOf(Guid novelId, string status) =>
        new() { Id = Guid.NewGuid(), NovelId = novelId, Title = "c", Slug = "c", Status = status, ChapterIndex = 1 };

    private void Setup(Chapter chapter, string? currentUserId)
    {
        novels.GetOne(novel.Id).Returns(novel);
        chapters.GetChapterById(chapter.Id).Returns(chapter);
        userContext.GetCurrentUser().Returns(currentUserId == null ? null : new CurrentUser(currentUserId, "e", "u", "d"));
        mapper.Map<ChapterSingleReaderDTO>(chapter).Returns(new ChapterSingleReaderDTO());
        mapper.Map<List<ChapterParagraphDTO>>(Arg.Any<object>()).Returns([]);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("some-reader")]
    public async Task Readers_cannot_open_a_draft_chapter(string? userId)
    {
        var draft = ChapterOf(novel.Id, "Draft");
        Setup(draft, userId);

        await Assert.ThrowsAsync<NotFoundException>(() =>
            Handler().Handle(new GetChapterReaderQuery(novel.Id, draft.Id), CancellationToken.None));
    }

    [Fact]
    public async Task A_chapter_cannot_be_read_through_another_novel()
    {
        var foreign = ChapterOf(Guid.NewGuid(), "Published");
        Setup(foreign, currentUserId: null);

        await Assert.ThrowsAsync<NotFoundException>(() =>
            Handler().Handle(new GetChapterReaderQuery(novel.Id, foreign.Id), CancellationToken.None));
    }

    [Fact]
    public async Task Readers_cannot_open_chapters_of_a_draft_novel()
    {
        novel.IsDraft = true;
        var chapter = ChapterOf(novel.Id, "Published");
        Setup(chapter, currentUserId: "some-reader");

        await Assert.ThrowsAsync<NotFoundException>(() =>
            Handler().Handle(new GetChapterReaderQuery(novel.Id, chapter.Id), CancellationToken.None));
    }

    [Fact]
    public async Task Authors_can_preview_their_own_drafts()
    {
        var draft = ChapterOf(novel.Id, "Draft");
        Setup(draft, currentUserId: novel.AuthorId);

        var result = await Handler().Handle(new GetChapterReaderQuery(novel.Id, draft.Id), CancellationToken.None);

        Assert.NotNull(result);
        Assert.False(result.IsLocked);
    }

    [Fact]
    public async Task Readers_get_published_chapters_and_the_read_is_tracked()
    {
        var chapter = ChapterOf(novel.Id, "Published");
        Setup(chapter, currentUserId: null);
        visitorContext.GetVisitorKey().Returns("a:guest");

        var result = await Handler().Handle(new GetChapterReaderQuery(novel.Id, chapter.Id), CancellationToken.None);

        Assert.NotNull(result);
        visitorContext.Received(1).GetVisitorKey();
    }
}
