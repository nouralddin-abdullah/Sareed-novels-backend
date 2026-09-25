using Application.Library.Commands.TrackProgress;
using Application.Library.Queries.GetMyLibrary;
using Application.ReadingLists.Commands.CreateReadingList;
using Application.Services;
using Application.Users;
using Domain.Entities;
using Domain.Library;
using Domain.Repositories;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Sareed_novels_backend.Tests.Unit;

public class LibraryHandlerTests
{
    private readonly ILibraryRepository library = Substitute.For<ILibraryRepository>();
    private readonly IChaptersRepository chapters = Substitute.For<IChaptersRepository>();
    private readonly INovelsRepository novels = Substitute.For<INovelsRepository>();
    private readonly IUserContext userContext = Substitute.For<IUserContext>();

    public LibraryHandlerTests()
    {
        userContext.GetCurrentUser().Returns(new CurrentUser("reader-1", "e", "reader", "Reader"));
    }

    private static ChapterOutline Outline(int index) => new(Guid.NewGuid(), $"Chapter {index}", index);

    [Theory]
    [InlineData(0, 20, 1, 20)]
    [InlineData(-3, 0, 1, 1)]
    [InlineData(2, 10_000, 2, GetMyLibraryQueryHandler.MaxPageSize)]
    public async Task Library_paging_is_clamped_before_it_reaches_the_database(int page, int size, int expectedPage, int expectedSize)
    {
        library.GetUserLibraryAsync("reader-1", Arg.Any<int>(), Arg.Any<int>()).Returns((Array.Empty<LibraryEntry>(), 0));
        var handler = new GetMyLibraryQueryHandler(NullLogger<GetMyLibraryQueryHandler>.Instance, library, userContext);

        var result = await handler.Handle(new GetMyLibraryQuery { PageNumber = page, PageSize = size }, CancellationToken.None);

        await library.Received(1).GetUserLibraryAsync("reader-1", expectedPage, expectedSize);
        Assert.Equal(0, result.TotalPages);
    }

    [Fact]
    public async Task The_library_points_continue_reading_at_a_chapter_readers_can_open()
    {
        var published = new[] { Outline(1), Outline(2) };
        var unpublished = Outline(3);
        var entry = new LibraryEntry(Guid.NewGuid(), "t", "s", "c", 4.5m, 10, "author", "Author", null,
            unpublished, DateTime.UtcNow, published);
        library.GetUserLibraryAsync("reader-1", 1, 20).Returns((new[] { entry }, 1));
        var handler = new GetMyLibraryQueryHandler(NullLogger<GetMyLibraryQueryHandler>.Instance, library, userContext);

        var result = await handler.Handle(new GetMyLibraryQuery(), CancellationToken.None);

        var item = Assert.Single(result.Items);
        Assert.Equal(published[1].Id, item.LastReadChapterId);
        Assert.Equal("Chapter 2", item.LastReadChapterTitle);
        Assert.Equal(2, item.LastReadChapterNumber);
        Assert.Equal(2, item.TotalChapters);
        Assert.Equal(100m, item.ProgressPercentage);
    }

    private TrackReadingProgressCommandHandler TrackHandler() =>
        new(NullLogger<TrackReadingProgressCommandHandler>.Instance, library, chapters, novels, userContext);

    private (Novel Novel, Chapter Chapter) PublishedChapter(bool novelIsDraft = false)
    {
        var novel = new Novel { Id = Guid.NewGuid(), AuthorId = "author-1", Title = "t", Slug = "s", Summary = "", CoverImageUrl = "", IsDraft = novelIsDraft };
        var chapter = new Chapter
        {
            Id = Guid.NewGuid(), NovelId = novel.Id, Title = "c", Slug = "c", Status = "Published", ChapterIndex = 4, PublishedChapterSequence = 3
        };
        chapters.GetChapterById(chapter.Id).Returns(chapter);
        novels.GetOne(novel.Id).Returns(novel);
        return (novel, chapter);
    }

    [Fact]
    public async Task Reading_a_chapter_saves_its_published_position()
    {
        var (novel, chapter) = PublishedChapter();

        var result = await TrackHandler().Handle(new TrackReadingProgressCommand(chapter.Id), CancellationToken.None);

        Assert.True(result.Success);
        await library.Received(1).SaveProgressAsync("reader-1", novel.Id, chapter.Id, 3, Arg.Any<DateTime>());
    }

    [Fact]
    public async Task Progress_is_not_saved_for_a_draft_novel()
    {
        var (_, chapter) = PublishedChapter(novelIsDraft: true);

        var result = await TrackHandler().Handle(new TrackReadingProgressCommand(chapter.Id), CancellationToken.None);

        Assert.False(result.Success);
        await library.DidNotReceiveWithAnyArgs().SaveProgressAsync(default!, default, default, default, default);
    }

    [Fact]
    public async Task Creating_a_list_with_a_name_already_used_is_a_failed_result_not_an_exception()
    {
        var lists = Substitute.For<IReadingListsRepository>();
        lists.IsNameTakenByUserAsync("reader-1", "Favourites").Returns(true);
        var handler = new CreateReadingListCommandHandler(
            NullLogger<CreateReadingListCommandHandler>.Instance, lists, userContext, Substitute.For<IFileUploadService>());

        var result = await handler.Handle(new CreateReadingListCommand { Name = "Favourites" }, CancellationToken.None);

        Assert.False(result.Success);
        Assert.Contains("already", result.Message);
        await lists.DidNotReceiveWithAnyArgs().CreateAsync(default!);
    }
}
