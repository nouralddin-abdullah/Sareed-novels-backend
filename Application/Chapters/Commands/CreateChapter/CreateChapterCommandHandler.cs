using Application.Chapters.DTOS;
using Application.Chapters.Paragraphs;
using Application.Services;
using Application.Users;
using Application.Users.Commands.FollowUser;
using AutoMapper;
using Domain.Constants;
using Domain.Entities;
using Domain.Exceptions;
using Domain.Repositories;
using Domain.Seo;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Application.Chapters.Commands.CreateChapter;

public class CreateChapterCommandHandler(
    ILogger<CreateChapterCommandHandler> logger, 
    IChaptersRepository chaptersRepository, 
    IUserContext userContext, 
    INovelsRepository novelsRepository, 
    IMapper mapper,
    IChapterSequenceService sequenceService,
    IServiceProvider serviceProvider,
    TimeProvider time) : IRequestHandler<CreateChapterCommand, ChapterSingleAuthorDTO>
{
    public async Task<ChapterSingleAuthorDTO> Handle(CreateChapterCommand request, CancellationToken cancellationToken)
    {
        logger.LogInformation("Adding new chapter for novel {NovelId}", request.NovelId);
        
        var currentUser = userContext.GetCurrentUser() ?? throw new ForbidException("سجّل الدخول للمتابعة", "NotSignedIn");
        var novel = await novelsRepository.GetOne(request.NovelId) ?? throw new NotFoundException("الرواية غير موجودة", "NovelNotFound");
        
        if (novel.AuthorId != currentUser.Id) 
            throw new ForbidException("هذا الإجراء متاح لكاتب الرواية فقط", "NotOwner");
        
        var now = time.GetUtcNow().UtcDateTime;
        var chapter = mapper.Map<Chapter>(request);
        chapter.ChapterIndex = await chaptersRepository.GetNextChapterIndex(novel.Id);
        chapter.Id = Guid.NewGuid();
        chapter.Slug = Slugs.For(chapter.Id, request.Title);
        // Created now; created published, it also comes out now (#33). Its first revision (#75).
        chapter.CreatedAt = now;
        chapter.UpdatedAt = now;
        chapter.Revision = 1;
        chapter.SetStatus(request.Status, now);
        
        // The text as chapter format v1 stores it (#74), one row per paragraph.
        var paragraphs = ChapterFormat.Parse(request.Content)
            .Select((paragraph, index) => ParagraphRows.New(chapter.Id, paragraph, index, now))
            .ToList();
        
        chapter.Paragraphs = paragraphs;
        chapter.ParagraphsCount = paragraphs.Count;
        
        var result = await chaptersRepository.CreateChapter(chapter);
        if (!result)
        {
            throw new InvalidOperationException("Failed to create the chapter");
        }

        // Created published, the chapter comes out now: the novel's last update moves to it and readers are told (#39).
        // A draft doesn't come out, and changes neither, until it is first published (UpdateChapterCommandHandler).
        var cameOut = chapter.Status == ChapterStatuses.Published;
        await novelsRepository.RefreshChapterCountAsync(novel.Id, lastUpdatedAt: cameOut ? chapter.PublishedAt : null);

        if (cameOut)
        {
            logger.LogInformation(
                "New Published chapter {ChapterId} created for novel {NovelId}, triggering sequence recalculation", 
                chapter.Id, novel.Id);
            
            await sequenceService.RecalculateSequencesForNovelAsync(novel.Id);
            
            // Trigger privilege update (extend lock window)
            var privilegeService = serviceProvider.GetRequiredService<IPrivilegeService>();
            await privilegeService.OnChapterPublishedAsync(novel.Id);
            
            // Fire-and-forget: Send notifications to users who have this novel in their library
            _ = SendNewChapterNotificationsInBackground(novel.Id, chapter.Id, chapter.Slug, chapter.Title);
        }

        var chapterDto = mapper.Map<ChapterSingleAuthorDTO>(chapter);
        
        logger.LogInformation("Chapter {ChapterId} created successfully with {ParagraphCount} paragraphs for novel {NovelId}", 
            chapter.Id, paragraphs.Count, novel.Id);
        
        return chapterDto;
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
