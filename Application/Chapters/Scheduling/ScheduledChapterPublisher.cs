using Application.Chapters.Publishing;
using Application.Services;
using Domain.Constants;
using Domain.Entities;
using Domain.Repositories;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Application.Chapters.Scheduling;

/// <summary>
/// Publishes the drafts whose scheduled time has come (<see cref="Chapter.PublishAt"/>, #77), each the way its author's
/// publish does: with the chapter held as for the author's save (<see cref="IChapterParagraphsRepository.BeginEditAsync"/>,
/// #75), so the two run one after the other, the chapter is read, given its new status (<see cref="Chapter.SetStatus"/>:
/// when it came out, the first time, and its schedule cleared) and stored by the author's save
/// (<see cref="IChaptersRepository.UpdateChapter"/>); then <see cref="ChapterStatusEffects"/> runs, as after the author's
/// save. Its title and text aren't touched, so neither its revision nor its <see cref="Chapter.UpdatedAt"/> moves. Each
/// chapter is published in a scope of its own, as a request has, and once: a run that comes after another run, or after
/// the author published, rescheduled or cancelled it, finds it no longer due and does nothing. Run by the scheduler
/// every minute (<c>ScheduledChapterPublishingService</c>) and, for one novel, before each request that reads it or its
/// chapters (<see cref="PublishDueChaptersBehavior{TRequest,TResponse}"/>), since the host may stop the app while it is
/// idle.
/// </summary>
public sealed class ScheduledChapterPublisher(
    IChaptersRepository chaptersRepository,
    IServiceScopeFactory scopeFactory,
    TimeProvider time,
    ILogger<ScheduledChapterPublisher> logger)
{
    /// <summary>How many due chapters are read at a time.</summary>
    public const int BatchSize = 50;

    /// <summary>Publishes every due chapter; answers how many this run published.</summary>
    public Task<int> PublishDueAsync(CancellationToken cancellationToken) => PublishAsync(null, null, cancellationToken);

    /// <summary>Publishes one novel's due chapters, the novel given by id or by slug; answers how many this run published.</summary>
    public Task<int> PublishDueAsync(Guid? novelId, string? novelSlug, CancellationToken cancellationToken) =>
        novelId is null && novelSlug is null
            ? Task.FromResult(0)
            : PublishAsync(novelId, novelSlug, cancellationToken);

    private async Task<int> PublishAsync(Guid? novelId, string? novelSlug, CancellationToken cancellationToken)
    {
        var published = 0;
        while (true)
        {
            var due = await chaptersRepository.GetDueChapterIdsAsync(time.GetUtcNow().UtcDateTime, BatchSize, novelId, novelSlug);
            var publishedNow = 0;
            foreach (var chapterId in due)
            {
                // Stopping is checked between chapters only: a chapter stored published gets everything a publish does.
                if (cancellationToken.IsCancellationRequested)
                {
                    return published + publishedNow;
                }

                if (await PublishAsync(chapterId))
                {
                    publishedNow++;
                }
            }

            published += publishedNow;
            // A full batch may have more behind it; another only after progress, so chapters this run can't publish
            // never keep it going.
            if (due.Count < BatchSize || publishedNow == 0)
            {
                return published;
            }
        }
    }

    /// <summary>
    /// Publishes one due chapter, with its own repositories: the sequences recalculated after a publish read the chapters
    /// a context tracks, which would hold the status a chapter published earlier in this run had before.
    /// </summary>
    private async Task<bool> PublishAsync(Guid chapterId)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var services = scope.ServiceProvider;
        var chapters = services.GetRequiredService<IChaptersRepository>();

        Chapter? chapter;
        ChapterSave save;
        await using (var edit = await services.GetRequiredService<IChapterParagraphsRepository>().BeginEditAsync(chapterId))
        {
            // Read with the chapter held: as the author's last save left it.
            chapter = await chapters.GetChapterById(chapterId);
            var now = time.GetUtcNow().UtcDateTime;
            if (chapter is not { Status: ChapterStatuses.Draft, PublishAt: { } publishAt } || publishAt > now)
            {
                return false; // published, rescheduled, cancelled or deleted meanwhile
            }

            chapter.SetStatus(ChapterStatuses.Published, now);
            save = await chapters.UpdateChapter(chapter);
            if (!save.Saved)
            {
                return false;
            }

            await edit.CommitAsync();
        }

        var effects = new ChapterStatusEffects(
            services.GetRequiredService<IChapterSequenceService>(), services.GetRequiredService<INovelsRepository>(), services, logger);
        await effects.ApplyAsync(chapter.NovelId, chapter, ChapterStatuses.Draft, save);

        logger.LogInformation("Published scheduled chapter {ChapterId} of novel {NovelId}{Again}", chapter.Id, chapter.NovelId,
            save.CameOut ? "" : " (it was published before, so readers aren't told again)");
        return true;
    }
}
