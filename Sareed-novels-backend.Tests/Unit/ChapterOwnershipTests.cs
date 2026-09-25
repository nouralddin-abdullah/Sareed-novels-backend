using Application.Chapters.Commands.DeleteChapter;
using Application.Chapters.Commands.UpdateChapter;
using Application.Chapters.DTOS;
using Application.Chapters.Queries.GetChapterAuthor;
using Application.Services;
using Application.Users;
using AutoMapper;
using Domain.Entities;
using Domain.Exceptions;
using Domain.Repositories;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Sareed_novels_backend.Tests.Unit;

/// <summary>
/// Author-side chapter endpoints take a novel id and a chapter id from the route. Owning the novel must not give
/// access to a chapter of some other author's novel.
/// </summary>
public class ChapterOwnershipTests
{
    private const string AttackerId = "attacker";

    private readonly IChaptersRepository chapters = Substitute.For<IChaptersRepository>();
    private readonly IChapterParagraphsRepository paragraphs = Substitute.For<IChapterParagraphsRepository>();
    private readonly ICommentsRepository comments = Substitute.For<ICommentsRepository>();
    private readonly INovelsRepository novels = Substitute.For<INovelsRepository>();
    private readonly IUserContext userContext = Substitute.For<IUserContext>();
    private readonly IMapper mapper = Substitute.For<IMapper>();
    private readonly IChapterSequenceService sequences = Substitute.For<IChapterSequenceService>();
    private readonly IServiceProvider services = Substitute.For<IServiceProvider>();

    private readonly Novel attackersNovel = new() { Id = Guid.NewGuid(), AuthorId = AttackerId, Title = "mine", Slug = "s", Summary = "", CoverImageUrl = "" };
    private readonly Chapter victimsChapter = new() { Id = Guid.NewGuid(), NovelId = Guid.NewGuid(), Title = "draft", Slug = "c", Status = "Draft", ChapterIndex = 1 };

    public ChapterOwnershipTests()
    {
        userContext.GetCurrentUser().Returns(new CurrentUser(AttackerId, "e", "u", "d"));
        novels.GetOne(attackersNovel.Id).Returns(attackersNovel);
        chapters.GetChapterById(victimsChapter.Id).Returns(victimsChapter);
    }

    [Fact]
    public async Task Updating_another_novels_chapter_through_your_own_novel_is_not_found()
    {
        var handler = new UpdateChapterCommandHandler(
            NullLogger<UpdateChapterCommandHandler>.Instance, chapters, paragraphs, comments, novels, userContext, mapper,
            sequences, services);

        await Assert.ThrowsAsync<NotFoundException>(() => handler.Handle(
            new UpdateChapterCommand(victimsChapter.Id, attackersNovel.Id, "hacked", "Published", "<p>defaced</p>"),
            CancellationToken.None));

        await chapters.DidNotReceiveWithAnyArgs().UpdateChapter(default!);
        await paragraphs.DidNotReceiveWithAnyArgs().DeleteParagraph(default);
        Assert.Equal("draft", victimsChapter.Title);
    }

    [Fact]
    public async Task Deleting_another_novels_chapter_through_your_own_novel_is_not_found()
    {
        var handler = new DeleteChapterCommandHandler(
            NullLogger<DeleteChapterCommandHandler>.Instance, novels, chapters, userContext, sequences, services);

        await Assert.ThrowsAsync<NotFoundException>(() => handler.Handle(
            new DeleteChapterCommand(attackersNovel.Id, victimsChapter.Id), CancellationToken.None));

        await chapters.DidNotReceiveWithAnyArgs().DeleteChapter(default!);
        await novels.DidNotReceiveWithAnyArgs().RefreshChapterCountAsync(default);
    }

    [Fact]
    public async Task Reading_another_novels_draft_through_your_own_novel_is_not_found()
    {
        var handler = new GetChapterAuthorQueryHandler(chapters, paragraphs, novels, userContext, mapper);

        await Assert.ThrowsAsync<NotFoundException>(() => handler.Handle(
            new GetChapterAuthorQuery(attackersNovel.Id, victimsChapter.Id), CancellationToken.None));

        await paragraphs.DidNotReceiveWithAnyArgs().GetChapterParagraphs(default);
    }

    [Fact]
    public async Task Authors_still_read_their_own_chapters()
    {
        var own = new Chapter { Id = Guid.NewGuid(), NovelId = attackersNovel.Id, Title = "own", Slug = "o", Status = "Draft", ChapterIndex = 1 };
        chapters.GetChapterById(own.Id).Returns(own);
        paragraphs.GetChapterParagraphs(own.Id).Returns([]);
        mapper.Map<ChapterSingleAuthorDTO>(own).Returns(new ChapterSingleAuthorDTO());
        var handler = new GetChapterAuthorQueryHandler(chapters, paragraphs, novels, userContext, mapper);

        var result = await handler.Handle(new GetChapterAuthorQuery(attackersNovel.Id, own.Id), CancellationToken.None);

        Assert.NotNull(result);
    }

    [Fact]
    public async Task Deleting_your_own_chapter_recounts_the_novels_chapters_in_sql()
    {
        var own = new Chapter { Id = Guid.NewGuid(), NovelId = attackersNovel.Id, Title = "own", Slug = "o", Status = "Draft", ChapterIndex = 1 };
        chapters.GetChapterById(own.Id).Returns(own);
        chapters.DeleteChapter(own).Returns(true);
        var handler = new DeleteChapterCommandHandler(
            NullLogger<DeleteChapterCommandHandler>.Instance, novels, chapters, userContext, sequences, services);

        Assert.True(await handler.Handle(new DeleteChapterCommand(attackersNovel.Id, own.Id), CancellationToken.None));

        await novels.Received(1).RefreshChapterCountAsync(attackersNovel.Id);
        await novels.DidNotReceiveWithAnyArgs().UpdateOne(default!);
    }
}
