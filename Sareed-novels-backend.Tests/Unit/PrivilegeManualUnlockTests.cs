using Application.Services;
using Domain.Entities;
using Domain.Repositories;
using Infrastructure.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Sareed_novels_backend.Tests.Unit;

/// <summary>
/// Readers see a chapter as locked when its sequence is at or past PrivilegeStartSequence. Manual unlock used to only
/// decrement CurrentLockedCount, so the author was told "Chapter unlocked!" while readers still saw it locked.
/// </summary>
public class PrivilegeManualUnlockTests
{
    private const string AuthorId = "author-1";

    private readonly INovelPrivilegeRepository privileges = Substitute.For<INovelPrivilegeRepository>();
    private readonly IPrivilegeSubscriptionRepository subscriptions = Substitute.For<IPrivilegeSubscriptionRepository>();
    private readonly INovelsRepository novels = Substitute.For<INovelsRepository>();
    private readonly IChaptersRepository chapters = Substitute.For<IChaptersRepository>();

    private readonly Novel novel = new() { Id = Guid.NewGuid(), AuthorId = AuthorId, Title = "t", Slug = "s", Summary = "", CoverImageUrl = "" };
    private readonly NovelPrivilege privilege;

    public PrivilegeManualUnlockTests()
    {
        // 15 published chapters, 11-15 locked.
        privilege = new NovelPrivilege { NovelId = novel.Id, IsEnabled = true, PrivilegeStartSequence = 11, CurrentLockedCount = 5 };
        novels.GetOne(novel.Id).Returns(novel);
        privileges.GetByNovelIdAsync(novel.Id).Returns(privilege);
    }

    private PrivilegeService Service() => new(
        NullLogger<PrivilegeService>.Instance, privileges, subscriptions, novels, chapters,
        Substitute.For<IWalletService>(), Substitute.For<ITransactionManager>(), Substitute.For<IServiceScopeFactory>());

    private Chapter PublishedChapter(int sequence)
    {
        var chapter = new Chapter
        {
            Id = Guid.NewGuid(), NovelId = novel.Id, Title = "c", Slug = "c", Status = "Published",
            ChapterIndex = sequence, PublishedChapterSequence = sequence
        };
        chapters.GetChapterById(chapter.Id).Returns(chapter);
        return chapter;
    }

    [Fact]
    public async Task Unlocking_the_first_locked_chapter_makes_it_readable_for_everyone()
    {
        var chapter = PublishedChapter(11);

        var result = await Service().ManuallyUnlockChapterAsync(chapter.Id, AuthorId);

        Assert.True(result.Success);
        Assert.Equal(12, privilege.PrivilegeStartSequence);
        Assert.Equal(4, privilege.CurrentLockedCount);
        Assert.False(await Service().IsChapterLockedAsync(chapter.Id, userId: "some-reader"));
        Assert.True(Service().IsChapterLockedBySequence(12, privilege));
        await privileges.Received(1).UpdateAsync(privilege);
    }

    [Fact]
    public async Task Unlocking_a_later_chapter_also_unlocks_the_locked_chapters_before_it()
    {
        var chapter = PublishedChapter(13);

        var result = await Service().ManuallyUnlockChapterAsync(chapter.Id, AuthorId);

        Assert.True(result.Success);
        Assert.Contains("11-13", result.Message);
        Assert.Equal(14, privilege.PrivilegeStartSequence);
        Assert.Equal(2, privilege.CurrentLockedCount);
        Assert.False(Service().IsChapterLockedBySequence(13, privilege));
        Assert.True(Service().IsChapterLockedBySequence(15, privilege));
    }

    [Fact]
    public async Task Unlocking_the_last_locked_chapter_leaves_nothing_locked()
    {
        PublishedChapter(11);
        var last = PublishedChapter(15);

        await Service().ManuallyUnlockChapterAsync(last.Id, AuthorId);

        Assert.Equal(0, privilege.CurrentLockedCount);
        Assert.False(Service().IsChapterLockedBySequence(15, privilege));
    }

    [Fact]
    public async Task A_free_chapter_is_reported_as_not_locked()
    {
        var chapter = PublishedChapter(5);

        var result = await Service().ManuallyUnlockChapterAsync(chapter.Id, AuthorId);

        Assert.False(result.Success);
        Assert.Equal(11, privilege.PrivilegeStartSequence);
        await privileges.DidNotReceive().UpdateAsync(Arg.Any<NovelPrivilege>());
    }

    [Fact]
    public async Task Only_the_author_can_unlock()
    {
        var chapter = PublishedChapter(11);

        var result = await Service().ManuallyUnlockChapterAsync(chapter.Id, "someone-else");

        Assert.False(result.Success);
        Assert.Equal(11, privilege.PrivilegeStartSequence);
        await privileges.DidNotReceive().UpdateAsync(Arg.Any<NovelPrivilege>());
    }
}
