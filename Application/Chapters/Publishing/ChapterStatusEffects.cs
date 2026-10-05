using Application.Services;
using Domain.Constants;
using Domain.Entities;
using Domain.Repositories;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Application.Chapters.Publishing;

/// <summary>
/// What publishing or unpublishing a chapter does beyond its row, once the save that changed its status is stored: the
/// novel's published sequences and readers' progress, its chapter count, and its last update when the chapter comes out
/// (#39); publishing also extends the privilege window, and readers are told when the chapter comes out for the first
/// time. The author's save (UpdateChapterCommandHandler) and the schedule (<c>ScheduledChapterPublisher</c>, #77) both
/// run it, so a chapter published on schedule comes out exactly as one published by hand.
/// </summary>
public sealed class ChapterStatusEffects(
    IChapterSequenceService sequenceService,
    INovelsRepository novelsRepository,
    IServiceProvider serviceProvider,
    ILogger logger)
{
    /// <summary>
    /// Runs the effects of <paramref name="save"/> for <paramref name="chapter"/>, as that save stored it (its status,
    /// slug and title; and <see cref="Chapter.PublishedAt"/>, read when it came out). Nothing unless the save changed the
    /// stored status (<see cref="ChapterSave.StatusChanged"/>): of two saves, or a save and the schedule, changing it at
    /// the same moment, the one that did runs them, once.
    /// </summary>
    public async Task ApplyAsync(Guid novelId, Chapter chapter, ChapterSave save)
    {
        if (!save.StatusChanged)
        {
            return;
        }

        logger.LogInformation(
            "Chapter {ChapterId} status changed to {NewStatus}, triggering sequence recalculation", chapter.Id, chapter.Status);

        await sequenceService.RecalculateSequencesForNovelAsync(novelId);
        await sequenceService.UpdateReadingProgressForNovelAsync(novelId);

        // The novel's ChapterCount counts published chapters, so publishing or unpublishing one changes it. Its last
        // update moves only when the chapter comes out, published for the first time (#39); unpublishing it, or
        // publishing it again after that, isn't an update to readers.
        await novelsRepository.RefreshChapterCountAsync(novelId, lastUpdatedAt: save.CameOut ? chapter.PublishedAt : null);

        if (chapter.Status != ChapterStatuses.Published)
        {
            return;
        }

        // Trigger privilege update: the chapter was published
        var privilegeService = serviceProvider.GetRequiredService<IPrivilegeService>();
        await privilegeService.OnChapterPublishedAsync(novelId);

        // Readers are told once, when the chapter comes out (#39): published again, it isn't new, as the library's
        // «فصول جديدة» doesn't show it again either. Fire-and-forget.
        if (save.CameOut)
        {
            _ = SendNewChapterNotificationsInBackground(novelId, chapter.Id, chapter.Slug, chapter.Title);
        }
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
