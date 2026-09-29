using System.Diagnostics;
using Application.Chapters.Paragraphs;
using Application.Services;
using Application.Users;
using Application.Users.Commands.FollowUser;
using AutoMapper;
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
    TimeProvider time) : IRequestHandler<UpdateChapterCommand, OperationResult>
{
    public async Task<OperationResult> Handle(UpdateChapterCommand request, CancellationToken cancellationToken)
    {
        logger.LogInformation("Updating chapter {ChapterId} of novel {NovelId}", request.ChapterId, request.NovelId);
        
        var currentUser = userContext.GetCurrentUser() ?? throw new ForbidException("سجّل الدخول للمتابعة", "NotSignedIn");
        var novel = await novelsRepository.GetOne(request.NovelId) ?? throw new NotFoundException("الرواية غير موجودة", "NovelNotFound");
        var chapter = await chaptersRepository.GetChapterById(request.ChapterId) ?? throw new NotFoundException("الفصل غير موجود", "ChapterNotFound");
        
        if (novel.AuthorId != currentUser.Id) throw new ForbidException("هذا الإجراء متاح لكاتب الرواية فقط", "NotOwner");

        // The chapter must belong to the novel the caller owns; otherwise any author could edit any chapter.
        if (chapter.NovelId != novel.Id) throw new NotFoundException("الفصل غير موجود", "ChapterNotFound");
        
        // Track if status is changing to/from Published
        var oldStatus = chapter.Status;
        var statusChanging = !string.IsNullOrEmpty(request.Status) && request.Status != oldStatus;
        var needsSequenceRecalculation = statusChanging && 
            (oldStatus == "Published" || request.Status == "Published");
        
        // Paragraphs first, while the chapter entity is unchanged: the paragraph transaction then holds the chapter
        // row only for its counter update at the end, not from its first write, while readers may be commenting.
        if (!string.IsNullOrEmpty(request.Content))
        {
            chapter.ParagraphsCount = await SaveParagraphs(chapter.Id, request.Content);
        }
        
        if (request.Title != null)
        {
            // The editor resends the unchanged title on every save; only a real rename may change the slug.
            var newSlug = Slugs.For(chapter.Id, request.Title);
            if (newSlug != Slugs.For(chapter.Id, chapter.Title))
            {
                chapter.Slug = newSlug;
            }
        }
        
        // Update basic fields, and the status through SetStatus: publishing a draft stamps when it comes out, the first
        // time only (#33).
        mapper.Map(request, chapter);
        if (request.Status != null)
        {
            chapter.SetStatus(request.Status, time.GetUtcNow().UtcDateTime);
        }
        
        var result = await chaptersRepository.UpdateChapter(chapter);
        
        // Recalculate sequences if status changed to/from Published
        if (result && needsSequenceRecalculation)
        {
            logger.LogInformation(
                "Chapter {ChapterId} status changed from {OldStatus} to {NewStatus}, triggering sequence recalculation", 
                chapter.Id, oldStatus, request.Status);
            
            await sequenceService.RecalculateSequencesForNovelAsync(request.NovelId);
            await sequenceService.UpdateReadingProgressForNovelAsync(request.NovelId);

            // The novel's ChapterCount counts published chapters, so publishing or unpublishing one changes it.
            await novelsRepository.RefreshChapterCountAsync(request.NovelId);

            // Trigger privilege update if status changed to Published
            if (request.Status == "Published" && oldStatus != "Published")
            {
                var privilegeService = serviceProvider.GetRequiredService<IPrivilegeService>();
                await privilegeService.OnChapterPublishedAsync(request.NovelId);
                
                // Fire-and-forget: Send notifications
                _ = SendNewChapterNotificationsInBackground(novel.Id, chapter.Id, chapter.Slug, chapter.Title);
            }
        }
        
        if (result)
        {
            return new OperationResult
            {
                Success = true,
                Message = "حُفظ الفصل"
            };
        }
        
        return new OperationResult
        {
            Success = false,
            Code = "OperationFailed",
            Message = "تعذّر حفظ الفصل. حاول مرة أخرى."
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
    /// Replaces the chapter's paragraphs with the edited content and returns how many there are now. A paragraph
    /// whose words are unchanged (<see cref="ParagraphText.VisibleText"/>) keeps its id and its comments, wherever
    /// it moved and whatever its formatting; a changed or deleted paragraph goes, and its comments with it.
    /// </summary>
    private async Task<int> SaveParagraphs(Guid chapterId, string content)
    {
        var stopwatch = Stopwatch.StartNew();
        var saved = await paragraphsRepository.GetChapterParagraphs(chapterId);
        var editedTexts = ParagraphText.Split(content);
        var match = ParagraphMatcher.Match(saved.Select(p => p.Content).ToList(), editedTexts);

        var now = DateTime.UtcNow;
        var reformatted = 0;
        var paragraphs = new List<ChapterParagraph>(editedTexts.Count);
        for (var index = 0; index < editedTexts.Count; index++)
        {
            var text = editedTexts[index];
            var savedIndex = match.SavedIndexByEdited[index];
            if (savedIndex < 0)
            {
                paragraphs.Add(new ChapterParagraph
                {
                    Id = Guid.NewGuid(),
                    ChapterId = chapterId,
                    Content = text,
                    ContentHash = ParagraphText.Hash(text),
                    OrderIndex = index,
                    ContentType = "text",
                    CreatedAt = now,
                    CommentsCount = 0
                });
                continue;
            }

            var paragraph = saved[savedIndex];
            var changed = false;
            if (paragraph.Content != text)
            {
                // Same words in new markup (bold, italic, line breaks, spacing): readers get the new markup.
                paragraph.Content = text;
                paragraph.ContentHash = ParagraphText.Hash(text);
                reformatted++;
                changed = true;
            }

            if (paragraph.OrderIndex != index)
            {
                paragraph.OrderIndex = index;
                changed = true;
            }

            if (changed)
            {
                paragraph.UpdatedAt = now;
            }

            paragraphs.Add(paragraph);
        }

        var removed = match.RemovedSaved.Select(i => saved[i]).ToList();
        var deleted = await paragraphsRepository.SaveEditedParagraphs(chapterId, paragraphs, removed);

        logger.Log(deleted.Visible > 0 ? LogLevel.Warning : LogLevel.Information,
            "Chapter {ChapterId} paragraphs saved: {Kept} kept, {Moved} moved, {Created} new, {Removed} removed " +
            "({Reformatted} kept with new formatting); {CommentsDeleted} comments deleted with the removed paragraphs " +
            "({VisibleCommentsDeleted} visible to readers); {ElapsedMs} ms",
            chapterId, match.Kept, match.Moved, match.Created, match.Removed, reformatted,
            deleted.Comments, deleted.Visible, stopwatch.ElapsedMilliseconds);

        return paragraphs.Count;
    }
}
