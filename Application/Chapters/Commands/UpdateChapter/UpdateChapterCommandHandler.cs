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

namespace Application.Chapters.Commands.UpdateChapter;

public class UpdateChapterCommandHandler(
    ILogger<UpdateChapterCommandHandler> logger, 
    IChaptersRepository chaptersRepository, 
    IChapterParagraphsRepository paragraphsRepository, 
    ICommentsRepository commentsRepository, 
    INovelsRepository novelsRepository, 
    IUserContext userContext, 
    IMapper mapper,
    IChapterSequenceService sequenceService,
    IServiceProvider serviceProvider) : IRequestHandler<UpdateChapterCommand, OperationResult>
{
    public async Task<OperationResult> Handle(UpdateChapterCommand request, CancellationToken cancellationToken)
    {
        logger.LogInformation("Updating chapter {@chapter}", request);
        
        var currentUser = userContext.GetCurrentUser() ?? throw new ForbidException("User not signed in");
        var novel = await novelsRepository.GetOne(request.NovelId) ?? throw new NotFoundException("This novel wasn't found");
        var chapter = await chaptersRepository.GetChapterById(request.ChapterId) ?? throw new NotFoundException("Chapter wasn't found");
        
        if (novel.AuthorId != currentUser.Id) throw new ForbidException("User doesn't own this novel");
        
        // Track if status is changing to/from Published
        var oldStatus = chapter.Status;
        var statusChanging = !string.IsNullOrEmpty(request.Status) && request.Status != oldStatus;
        var needsSequenceRecalculation = statusChanging && 
            (oldStatus == "Published" || request.Status == "Published");
        
        if (request.Title != null)
        {
            chapter.Slug = $"{chapter.Id.ToString()[..5]}-{request.Title.Replace(" ", "-").ToLower()}";
        }
        
        // Update basic fields
        mapper.Map(request, chapter);
        
        // Handle content update - O(n) hash-based matching
        if (!string.IsNullOrEmpty(request.Content))
        {
            logger.LogInformation("Updating paragraphs for chapter {ChapterId}", chapter.Id);
            
            var sw = System.Diagnostics.Stopwatch.StartNew();
            
            // Get existing paragraphs
            var existingParagraphs = await paragraphsRepository.GetChapterParagraphs(chapter.Id);
            
            // Split new content into paragraphs
            var newParagraphTexts = SplitIntoParagraphs(request.Content);
            
            // O(n) hash-based matching
            var matchResult = MatchParagraphsByHash(existingParagraphs, newParagraphTexts);
            
            sw.Stop();
            logger.LogDebug("Paragraph matching took {ElapsedMs}ms for {ParagraphCount} paragraphs", 
                sw.ElapsedMilliseconds, existingParagraphs.Count);
            
            // Delete paragraphs that were removed or changed
            foreach (var paragraphToDelete in matchResult.ParagraphsToDelete)
            {
                await commentsRepository.DeleteParagraphComments(paragraphToDelete.Id);
                await paragraphsRepository.DeleteParagraph(paragraphToDelete.Id);
            }
            
            // Update order index for unchanged paragraphs (if moved)
            foreach (var (paragraph, newIndex) in matchResult.ParagraphsToUpdate)
            {
                if (paragraph.OrderIndex != newIndex)
                {
                    paragraph.OrderIndex = newIndex;
                    paragraph.UpdatedAt = DateTime.UtcNow;
                    await paragraphsRepository.UpdateParagraph(paragraph);
                }
            }
            
            // Create new paragraphs
            if (matchResult.ParagraphsToCreate.Any())
            {
                await paragraphsRepository.CreateParagraphs(matchResult.ParagraphsToCreate);
            }
            
            // Update paragraph count
            chapter.ParagraphsCount = newParagraphTexts.Count;
            
            logger.LogInformation(
                "Chapter {ChapterId} updated: {UnchangedCount} preserved, {ChangedCount} changed, {DeletedCount} deleted, {NewCount} new",
                chapter.Id, 
                matchResult.ParagraphsToUpdate.Count, 
                matchResult.ParagraphsToDelete.Count - matchResult.ParagraphsToUpdate.Count,
                matchResult.ParagraphsToDelete.Count, 
                matchResult.ParagraphsToCreate.Count);
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
            
            // Fire-and-forget: If status changed to Published, send notifications
            if (request.Status == "Published" && oldStatus != "Published")
            {
                _ = SendNewChapterNotificationsInBackground(novel.Id, chapter.Id, chapter.Slug, chapter.Title);
            }
        }
        
        if (result)
        {
            return new OperationResult
            {
                Success = true,
                Message = "Update chapter is successful"
            };
        }
        
        return new OperationResult
        {
            Success = false,
            Message = "Update chapter is not successful"
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
    
    private static List<string> SplitIntoParagraphs(string content)
    {
        return content
            .Split(new[] { "\n\n", "\r\n\r\n", "</p><p>", "</p>" }, StringSplitOptions.RemoveEmptyEntries)
            .Select(p => p.Trim()
                .Replace("<p>", "")
                .Replace("</p>", ""))
            // Keep <br> tags to preserve line breaks within paragraphs
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .ToList();
    }
    
    private static ParagraphMatchResult MatchParagraphsByHash(
        List<ChapterParagraph> existingParagraphs, 
        List<string> newParagraphTexts)
    {
        // Build hash dictionary from existing paragraphs - O(n)
        var existingByHash = new Dictionary<string, ChapterParagraph>();
        foreach (var para in existingParagraphs)
        {
            if (!existingByHash.ContainsKey(para.ContentHash))
            {
                existingByHash[para.ContentHash] = para;
            }
        }
        
        var paragraphsToUpdate = new List<(ChapterParagraph paragraph, int newIndex)>();
        var paragraphsToCreate = new List<ChapterParagraph>();
        var usedHashes = new HashSet<string>();
        
        // Match new paragraphs with existing ones - O(n)
        for (int newIndex = 0; newIndex < newParagraphTexts.Count; newIndex++)
        {
            var newText = newParagraphTexts[newIndex];
            var newHash = ComputeContentHash(newText);
            
            // Check if this exact content exists
            if (existingByHash.TryGetValue(newHash, out var existingParagraph) && 
                !usedHashes.Contains(newHash))
            {
                // Reuse existing paragraph (preserve comments)
                paragraphsToUpdate.Add((existingParagraph, newIndex));
                usedHashes.Add(newHash);
            }
            else
            {
                // Create new paragraph
                paragraphsToCreate.Add(new ChapterParagraph
                {
                    Id = Guid.NewGuid(),
                    ChapterId = existingParagraphs.FirstOrDefault()?.ChapterId ?? Guid.Empty,
                    Content = newText,
                    ContentHash = newHash,
                    OrderIndex = newIndex,
                    ContentType = "text",
                    CreatedAt = DateTime.UtcNow,
                    CommentsCount = 0
                });
            }
        }
        
        // Identify paragraphs to delete (not matched)
        var matchedParagraphIds = paragraphsToUpdate.Select(x => x.paragraph.Id).ToHashSet();
        var paragraphsToDelete = existingParagraphs
            .Where(p => !matchedParagraphIds.Contains(p.Id))
            .ToList();
        
        return new ParagraphMatchResult
        {
            ParagraphsToUpdate = paragraphsToUpdate,
            ParagraphsToCreate = paragraphsToCreate,
            ParagraphsToDelete = paragraphsToDelete
        };
    }
    
    private static string ComputeContentHash(string content)
    {
        // Normalize content before hashing (same as CreateChapterCommandHandler)
        var normalized = content.Trim()
            .Replace("\r\n", "\n")
            .Replace("\r", "\n")
            .Replace("\t", " ");
        
        using var sha256 = SHA256.Create();
        var bytes = Encoding.UTF8.GetBytes(normalized);
        var hashBytes = sha256.ComputeHash(bytes);
        return Convert.ToBase64String(hashBytes);
    }
    
    private class ParagraphMatchResult
    {
        public List<(ChapterParagraph paragraph, int newIndex)> ParagraphsToUpdate { get; set; } = new();
        public List<ChapterParagraph> ParagraphsToCreate { get; set; } = new();
        public List<ChapterParagraph> ParagraphsToDelete { get; set; } = new();
    }
}
