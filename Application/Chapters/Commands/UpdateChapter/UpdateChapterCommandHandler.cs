using System.Diagnostics;
using Application.Chapters.Paragraphs;
using Application.Services;
using Application.Users;
using AutoMapper;
using Domain.Constants;
using Domain.Entities;
using Domain.Exceptions;
using Domain.Repositories;
using Domain.Seo;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Application.Chapters.Commands.UpdateChapter;

public class UpdateChapterCommandHandler(
    ILogger<UpdateChapterCommandHandler> logger, 
    IChaptersRepository chaptersRepository, 
    IChapterParagraphsRepository paragraphsRepository, 
    INovelsRepository novelsRepository, 
    IUserContext userContext, 
    IMapper mapper,
    IChapterSequenceService sequenceService,
    IServiceProvider serviceProvider,
    TimeProvider time) : IRequestHandler<UpdateChapterCommand, UpdateChapterResult>
{
    public async Task<UpdateChapterResult> Handle(UpdateChapterCommand request, CancellationToken cancellationToken)
    {
        logger.LogInformation("Updating chapter {ChapterId} of novel {NovelId}", request.ChapterId, request.NovelId);
        
        var currentUser = userContext.GetCurrentUser() ?? throw new ForbidException("سجّل الدخول للمتابعة", "NotSignedIn");
        var novel = await novelsRepository.GetOne(request.NovelId) ?? throw new NotFoundException("الرواية غير موجودة", "NovelNotFound");
        var chapter = await chaptersRepository.GetChapterById(request.ChapterId) ?? throw new NotFoundException("الفصل غير موجود", "ChapterNotFound");
        
        if (novel.AuthorId != currentUser.Id) throw new ForbidException("هذا الإجراء متاح لكاتب الرواية فقط", "NotOwner");

        // The chapter must belong to the novel the caller owns; otherwise any author could edit any chapter.
        if (chapter.NovelId != novel.Id) throw new NotFoundException("الفصل غير موجود", "ChapterNotFound");

        // The text as chapter format v1 stores it (#74), parsed before the chapter's text is held. A save with neither
        // a title nor text changes the status alone (#75).
        var edited = string.IsNullOrEmpty(request.Content) ? null : ChapterFormat.Parse(request.Content);
        var savesText = request.Title != null || edited != null;

        // One save of a chapter at a time, from any device (and the format maintenance): the chapter is read again
        // inside the edit, so the revision checked here is the one this save writes over (#75).
        await using var edit = await paragraphsRepository.BeginEditAsync(chapter.Id);
        if (!await chaptersRepository.ReloadAsync(chapter))
        {
            throw new NotFoundException("الفصل غير موجود", "ChapterNotFound");
        }

        // A copy older than the chapter mustn't overwrite newer text (#75): the editor reloads it first. Nothing is saved.
        if (savesText && request.BaseRevision is { } baseRevision && baseRevision != chapter.Revision)
        {
            throw new ChapterChangedException(chapter.Revision);
        }

        var match = edited is null ? null : ParagraphMatcher.Match(edit.Paragraphs.Select(ParagraphRows.Read).ToList(), edited);
        if (request.DryRun)
        {
            // Everything checked and matched; disposing the edit lets it go with nothing written.
            return new UpdateChapterResult
            {
                Success = true, Message = "لم يُحفظ شيء", Revision = chapter.Revision,
                Preview = await Preview(edit, match)
            };
        }

        // Track if status is changing to/from Published
        var oldStatus = chapter.Status;
        var statusChanging = !string.IsNullOrEmpty(request.Status) && request.Status != oldStatus;
        var needsSequenceRecalculation = statusChanging && 
            (oldStatus == "Published" || request.Status == "Published");
        var now = time.GetUtcNow().UtcDateTime;
        
        // Paragraphs first, while the chapter entity is unchanged: the edit then holds the chapter row only from its
        // updates at the end, not from its first write, while readers may be commenting.
        var textChanged = false;
        if (edited != null)
        {
            (chapter.ParagraphsCount, textChanged) = await SaveParagraphs(edit, chapter.Id, edited, match!, now);
            // The text is its paragraphs now; a copy left in the legacy Chapters.Content column is stale.
            chapter.Content = null;
        }
        
        if (request.Title != null)
        {
            textChanged |= request.Title != chapter.Title;
            // The editor resends the unchanged title on every save; only a real rename may change the slug.
            var newSlug = Slugs.For(chapter.Id, request.Title);
            if (newSlug != Slugs.For(chapter.Id, chapter.Title))
            {
                chapter.Slug = newSlug;
            }
        }
        
        // Update basic fields, and the status through SetStatus: publishing a draft stamps when it comes out, the first
        // time only (#33). UpdateChapter stores that stamp only while the chapter has none, and says whether this save
        // is the one that brought the chapter out (#39).
        mapper.Map(request, chapter);
        if (request.Status != null)
        {
            chapter.SetStatus(request.Status, now);
        }

        // The revision counts the saves that change the title or the text; a change of status alone, or the title and
        // text sent back as they are, leaves it (#75). Saved in the edit, over the revision read in it.
        if (textChanged)
        {
            chapter.Revision++;
        }

        chapter.UpdatedAt = now;
        
        var saved = await chaptersRepository.UpdateChapter(chapter);
        if (!saved.Saved)
        {
            return new UpdateChapterResult
            {
                Success = false,
                Code = "OperationFailed",
                Message = "تعذّر حفظ الفصل. حاول مرة أخرى."
            };
        }

        await edit.CommitAsync();

        // Recalculate sequences if status changed to/from Published
        if (needsSequenceRecalculation)
        {
            logger.LogInformation(
                "Chapter {ChapterId} status changed from {OldStatus} to {NewStatus}, triggering sequence recalculation",
                chapter.Id, oldStatus, request.Status);

            await sequenceService.RecalculateSequencesForNovelAsync(request.NovelId);
            await sequenceService.UpdateReadingProgressForNovelAsync(request.NovelId);

            // The novel's ChapterCount counts published chapters, so publishing or unpublishing one changes it. Its last
            // update moves only when the chapter comes out, published for the first time (#39); unpublishing it, or
            // publishing it again after that, isn't an update to readers.
            await novelsRepository.RefreshChapterCountAsync(
                request.NovelId, lastUpdatedAt: saved.CameOut ? chapter.PublishedAt : null);

            // Trigger privilege update if status changed to Published
            if (request.Status == ChapterStatuses.Published && oldStatus != ChapterStatuses.Published)
            {
                var privilegeService = serviceProvider.GetRequiredService<IPrivilegeService>();
                await privilegeService.OnChapterPublishedAsync(request.NovelId);

                // Readers are told once, when the chapter comes out (#39): published again, it isn't new, as the
                // library's «فصول جديدة» doesn't show it again either. Fire-and-forget.
                if (saved.CameOut)
                {
                    _ = SendNewChapterNotificationsInBackground(novel.Id, chapter.Id, chapter.Slug, chapter.Title);
                }
            }
        }

        return new UpdateChapterResult
        {
            Success = true,
            Message = "حُفظ الفصل",
            Revision = chapter.Revision
        };
    }

    /// <summary>
    /// What saving the match would delete (#75, a dry run): the saved paragraphs it removes, in order, with the comments
    /// that would go with each.
    /// </summary>
    private static async Task<ChapterSavePreview> Preview(IChapterTextEdit edit, ParagraphMatch? match)
    {
        var removed = match?.RemovedSaved.Select(i => edit.Paragraphs[i]).ToList() ?? [];
        var comments = await edit.CountCommentsToDeleteAsync(removed.Select(p => p.Id).ToList());
        return new ChapterSavePreview
        {
            ParagraphsRemoved = removed.Count,
            CommentsDeleted = comments.Values.Sum(),
            Removed = removed.Select(p => new RemovedParagraphPreview { ParagraphId = p.Id, CommentsCount = comments[p.Id] }).ToList()
        };
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
    
    /// <summary>
    /// Replaces the chapter's paragraphs with the edited ones, in chapter format v1 (#74), as <paramref name="match"/>
    /// paired them, and returns how many there are now and whether anything changed. A paragraph whose words are
    /// unchanged (<see cref="FormattedParagraph.MatchKey"/>) keeps its id and its comments, wherever it moved and
    /// whatever its kind or formatting, which are updated; a changed or deleted paragraph goes, and its comments with it.
    /// </summary>
    private async Task<(int Count, bool Changed)> SaveParagraphs(
        IChapterTextEdit edit, Guid chapterId, IReadOnlyList<FormattedParagraph> edited, ParagraphMatch match, DateTime now)
    {
        var stopwatch = Stopwatch.StartNew();
        var saved = edit.Paragraphs;
        var reformatted = 0;
        var reordered = false;
        var paragraphs = new List<ChapterParagraph>(edited.Count);
        for (var index = 0; index < edited.Count; index++)
        {
            var savedIndex = match.SavedIndexByEdited[index];
            if (savedIndex < 0)
            {
                paragraphs.Add(ParagraphRows.New(chapterId, edited[index], index, now));
                continue;
            }

            var paragraph = saved[savedIndex];
            var changed = false;
            // Same words in another kind or markup (center, bold, line breaks, spacing): readers get the new one.
            if (ParagraphRows.Store(paragraph, edited[index]))
            {
                reformatted++;
                changed = true;
            }

            if (paragraph.OrderIndex != index)
            {
                paragraph.OrderIndex = index;
                reordered = true;
                changed = true;
            }

            if (changed)
            {
                paragraph.UpdatedAt = now;
            }

            paragraphs.Add(paragraph);
        }

        var removed = match.RemovedSaved.Select(i => saved[i]).ToList();
        var deleted = await edit.SaveAsync(paragraphs, removed);

        logger.Log(deleted.Visible > 0 ? LogLevel.Warning : LogLevel.Information,
            "Chapter {ChapterId} paragraphs saved: {Kept} kept, {Moved} moved, {Created} new, {Removed} removed " +
            "({Reformatted} kept with new formatting); {CommentsDeleted} comments deleted with the removed paragraphs " +
            "({VisibleCommentsDeleted} visible to readers); {ElapsedMs} ms",
            chapterId, match.Kept, match.Moved, match.Created, match.Removed, reformatted,
            deleted.Comments, deleted.Visible, stopwatch.ElapsedMilliseconds);

        return (paragraphs.Count, match.Created > 0 || match.Removed > 0 || reformatted > 0 || reordered);
    }
}
