using Application.Chapters.DTOS;
using Application.Services;
using Application.Users;
using Application.Users.Commands.FollowUser;
using AutoMapper;
using Domain.Entities;
using Domain.Exceptions;
using Domain.Repositories;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Security.Cryptography;
using System.Text;

namespace Application.Chapters.Commands.CreateChapter;

public class CreateChapterCommandHandler(
    ILogger<CreateChapterCommandHandler> logger, 
    IChaptersRepository chaptersRepository, 
    IUserContext userContext, 
    INovelsRepository novelsRepository, 
    IMapper mapper,
    IChapterSequenceService sequenceService,
    ISearchIndexQueueService searchIndexQueue,
    IServiceProvider serviceProvider) : IRequestHandler<CreateChapterCommand, ChapterSingleAuthorDTO>
{
    public async Task<ChapterSingleAuthorDTO> Handle(CreateChapterCommand request, CancellationToken cancellationToken)
    {
        logger.LogInformation("Adding new chapter for novel {NovelId}", request.NovelId);
        
        var currentUser = userContext.GetCurrentUser() ?? throw new ForbidException("User not signed in");
        var novel = await novelsRepository.GetOne(request.NovelId) ?? throw new NotFoundException("This novel wasn't found");
        
        if (novel.AuthorId != currentUser.Id) 
            throw new ForbidException("User doesn't own this novel");
        
        var chapter = mapper.Map<Chapter>(request);
        chapter.ChapterIndex = await chaptersRepository.GetNextChapterIndex(novel.Id);
        chapter.Id = Guid.NewGuid();
        chapter.Slug = $"{chapter.Id.ToString()[..5]}-{request.Title.Replace(" ", "-").ToLower()}";
        
        // Split content into paragraphs
        var paragraphTexts = SplitIntoParagraphs(request.Content);
        var paragraphs = paragraphTexts.Select((text, index) => new ChapterParagraph
        {
            Id = Guid.NewGuid(),
            ChapterId = chapter.Id,
            Content = text,
            ContentHash = ComputeContentHash(text),
            OrderIndex = index,
            ContentType = "text",
            CreatedAt = DateTime.UtcNow,
            CommentsCount = 0
        }).ToList();
        
        chapter.Paragraphs = paragraphs;
        chapter.ParagraphsCount = paragraphs.Count;
        chapter.Content = null;
        
        var result = await chaptersRepository.CreateChapter(chapter);
        if (!result)
        {
            throw new InvalidOperationException("Failed to create the chapter");
        }

        novel.ChapterCount++;
        novel.LastUpdatedAt = DateTime.UtcNow;
        await novelsRepository.UpdateOne(novel);
        
        // If chapter is Published, recalculate sequences
        if (chapter.Status == "Published")
        {
            logger.LogInformation(
                "New Published chapter {ChapterId} created for novel {NovelId}, triggering sequence recalculation", 
                chapter.Id, novel.Id);
            
            await sequenceService.RecalculateSequencesForNovelAsync(novel.Id);
            
            // Fire-and-forget: Send notifications to users who have this novel in their library
            _ = SendNewChapterNotificationsInBackground(novel.Id, chapter.Id, chapter.Slug, chapter.Title);
        }
        
        // Queue for Elasticsearch update (ChapterCount changed)
        await searchIndexQueue.QueueUpdateAsync(novel.Id);
        logger.LogDebug("Queued novel {NovelId} for search index update (chapter added)", novel.Id);
        
        var chapterDto = mapper.Map<ChapterSingleAuthorDTO>(chapter);
        
        logger.LogInformation("Chapter {ChapterId} created successfully with {ParagraphCount} paragraphs for novel {NovelId}", 
            chapter.Id, paragraphs.Count, novel.Id);
        
        return chapterDto;
    }
    
    private static List<string> SplitIntoParagraphs(string content)
    {
        return content
            .Split(new[] { "\n\n", "\r\n\r\n", "</p><p>", "</p>" }, StringSplitOptions.RemoveEmptyEntries)
            .Select(p => p.Trim()
                .Replace("<p>", "")
                .Replace("</p>", "")
                .Replace("<br>", "")
                .Replace("<br/>", ""))
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .ToList();
    }
    
    private static string ComputeContentHash(string content)
    {
        var normalized = content.Trim()
            .Replace("\r\n", "\n")
            .Replace("\r", "\n")
            .Replace("\t", " ");
        
        using var sha256 = SHA256.Create();
        var bytes = Encoding.UTF8.GetBytes(normalized);
        var hashBytes = sha256.ComputeHash(bytes);
        return Convert.ToBase64String(hashBytes);
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
