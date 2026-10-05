using Application.Services;
using Domain.Constants;
using Domain.Entities;
using Domain.Repositories;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Application.Chapters.Publishing;

/// <summary>
/// What publishing or unpublishing a chapter does beyond its row, once the save that changed its status is committed:
/// the novel's published sequences and readers' progress, its chapter count, and its last update when the chapter comes
/// out (#39); publishing also extends the privilege window, and readers are told when the chapter comes out for the
/// first time, unless the novel is hidden (<see cref="AnnounceNewChapterAsync"/>, #80). The author's save
/// (UpdateChapterCommandHandler) and the schedule (<c>ScheduledChapterPublisher</c>, #77) both run it, so a chapter
/// published on schedule comes out exactly as one published by hand; a chapter created published is announced by it too.
/// </summary>
public sealed class ChapterStatusEffects(
    IChapterSequenceService sequenceService,
    INovelsRepository novelsRepository,
    IServiceProvider serviceProvider,
    ILogger logger)
{
    /// <summary>
    /// Runs the effects of <paramref name="save"/> for <paramref name="chapter"/>, as that save stored it (its status,
    /// slug and title, and <see cref="Chapter.PublishedAt"/>, read when it came out). Nothing unless the save published
    /// or unpublished it: its status before the save, <paramref name="statusBefore"/>, read with the chapter held for the
    /// save (ChapterParagraphsRepository.BeginEditAsync), so of two saves, or a save and the schedule, publishing it at
    /// the same moment, only the one that changed the status runs them.
    /// </summary>
    public async Task ApplyAsync(Guid novelId, Chapter chapter, string statusBefore, ChapterSave save)
    {
        var published = chapter.Status == ChapterStatuses.Published;
        if (chapter.Status == statusBefore || (!published && statusBefore != ChapterStatuses.Published))
        {
            return;
        }

        logger.LogInformation(
            "Chapter {ChapterId} status changed from {OldStatus} to {NewStatus}, triggering sequence recalculation",
            chapter.Id, statusBefore, chapter.Status);

        await sequenceService.RecalculateSequencesForNovelAsync(novelId);
        await sequenceService.UpdateReadingProgressForNovelAsync(novelId);

        // The novel's ChapterCount counts published chapters, so publishing or unpublishing one changes it. Its last
        // update moves only when the chapter comes out, published for the first time (#39); unpublishing it, or
        // publishing it again after that, isn't an update to readers.
        await novelsRepository.RefreshChapterCountAsync(novelId, lastUpdatedAt: save.CameOut ? chapter.PublishedAt : null);

        if (!published)
        {
            return;
        }

        // Trigger privilege update: the chapter was published
        var privilegeService = serviceProvider.GetRequiredService<IPrivilegeService>();
        await privilegeService.OnChapterPublishedAsync(novelId);

        // Readers are told once, when the chapter comes out (#39): published again, it isn't new, as the library's
        // «فصول جديدة» doesn't show it again either.
        if (save.CameOut)
        {
            await AnnounceNewChapterAsync(novelId, chapter);
        }
    }

    /// <summary>
    /// Tells the readers who have the novel in their library that <paramref name="chapter"/> came out (#39): their
    /// notification and push, sent in the background (fire-and-forget). Nobody while the novel is hidden (a draft, #80):
    /// its readers can't open it. A chapter is announced when it comes out, once, so publishing the novel later doesn't
    /// announce the chapters that came out while it was hidden.
    /// </summary>
    public async Task AnnounceNewChapterAsync(Guid novelId, Chapter chapter)
    {
        if (await novelsRepository.IsDraftAsync(novelId))
        {
            logger.LogInformation(
                "Chapter {ChapterId} came out while novel {NovelId} is hidden: its readers aren't told", chapter.Id, novelId);
            return;
        }

        _ = SendNewChapterNotificationsInBackground(novelId, chapter.Id, chapter.Slug, chapter.Title);
    }

    private async Task SendNewChapterNotificationsInBackground(Guid novelId, Guid chapterId, string chapterSlug, string chapterTitle)
    {
        try
        {
            using var scope = serviceProvider.CreateScope();
            var backgroundLibraryRepository = scope.ServiceProvider.GetRequiredService<ILibraryRepository>();
            var backgroundNotificationService = scope.ServiceProvider.GetRequiredService<INotificationService>();
            var backgroundNovelsRepository = scope.ServiceProvider.GetRequiredService<INovelsRepository>();

            var novel = await backgroundNovelsRepository.GetOne(novelId);
            if (novel == null) return;

            var chapter = new Chapter
            {
                Id = chapterId,
                Slug = chapterSlug,
                Title = chapterTitle,
                NovelId = novelId
            };

            var userIds = await backgroundLibraryRepository.GetUsersWithNovelInLibrary(novelId);

            if (userIds.Any())
            {
                await backgroundNotificationService.SendNewChapterInLibraryNotification(userIds, novel, chapter);
                logger.LogDebug("Sent NewChapterInLibrary notifications to {Count} users", userIds.Count);
            }
            else
            {
                logger.LogDebug("No users have novel {NovelId} in their library", novelId);
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to send NewChapterInLibrary notifications for chapter {ChapterId}", chapterId);
        }
    }
}
