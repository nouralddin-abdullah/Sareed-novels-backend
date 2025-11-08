using Application.Users;
using Application.Users.Commands.FollowUser;
using Domain.Entities;
using Domain.Exceptions;
using Domain.Repositories;
using MediatR;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Application.Library.Commands.TrackProgress;

public class TrackReadingProgressCommandHandler(
    ILogger<TrackReadingProgressCommandHandler> logger,
    ILibraryRepository libraryRepository,
    IChaptersRepository chaptersRepository,
    IUserContext userContext,
    IServiceProvider serviceProvider) : IRequestHandler<TrackReadingProgressCommand, OperationResult>
{
    public async Task<OperationResult> Handle(TrackReadingProgressCommand request, CancellationToken cancellationToken)
    {
        var currentUser = userContext.GetCurrentUser() ?? throw new ForbidException("User not signed in");

        var chapter = await chaptersRepository.GetChapterById(request.ChapterId)
            ?? throw new NotFoundException("Chapter not found");

        if (chapter.Status != "Published")
        {
            return new OperationResult
            {
                Success = false,
                Message = "Cannot track progress for unpublished chapters"
            };
        }

        // Use the cached PublishedChapterSequence
        var publishedSequenceNumber = chapter.PublishedChapterSequence;

        if (publishedSequenceNumber == null || publishedSequenceNumber <= 0)
        {
            logger.LogWarning(
                "Chapter {ChapterId} is Published but has no PublishedChapterSequence. Novel: {NovelId}", 
                chapter.Id, chapter.NovelId);
            
            // Fallback: Calculate from published chapters (but this shouldn't happen)
            var publishedChapters = await chaptersRepository.GetChaptersReaderView(chapter.NovelId);
            var publishedChaptersList = publishedChapters.OrderBy(c => c.ChapterIndex).ToList();
            publishedSequenceNumber = publishedChaptersList.FindIndex(c => c.Id == chapter.Id) + 1;

            if (publishedSequenceNumber == 0)
            {
                return new OperationResult
                {
                    Success = false,
                    Message = "Chapter not found in published chapters"
                };
            }
        }

        logger.LogInformation("User {UserId} tracking progress for novel {NovelId}, chapter {ChapterIndex} (cached sequence: {SequenceNumber})",
            currentUser.Id, chapter.NovelId, chapter.ChapterIndex, publishedSequenceNumber);

        var existingProgress = await libraryRepository.GetProgressAsync(currentUser.Id, chapter.NovelId);

        if (existingProgress == null)
        {
            var newProgress = new UserNovelProgress
            {
                UserId = currentUser.Id,
                NovelId = chapter.NovelId,
                LastReadChapterId = chapter.Id,
                LastReadChapterNumber = publishedSequenceNumber.Value,
                LastReadAt = DateTime.UtcNow,
                CreatedAt = DateTime.UtcNow
            };

            await libraryRepository.TrackProgressAsync(newProgress);
            
            // Fire-and-forget: Increment user's library novels count
            _ = IncrementUserLibraryNovelsCountInBackground(currentUser.Id);
            
            logger.LogInformation("Created new reading progress for user {UserId}, novel {NovelId}", currentUser.Id, chapter.NovelId);
        }
        else
        {
            existingProgress.LastReadChapterId = chapter.Id;
            existingProgress.LastReadChapterNumber = publishedSequenceNumber.Value;
            existingProgress.LastReadAt = DateTime.UtcNow;

            await libraryRepository.UpdateProgressAsync(existingProgress);
            logger.LogInformation("Updated reading progress for user {UserId}, novel {NovelId}", currentUser.Id, chapter.NovelId);
        }

        return new OperationResult
        {
            Success = true,
            Message = "Reading progress tracked successfully"
        };
    }
    
    private async Task IncrementUserLibraryNovelsCountInBackground(string userId)
    {
        try
        {
            using var scope = serviceProvider.CreateScope();
            var backgroundUserManager = scope.ServiceProvider.GetRequiredService<UserManager<User>>();
            
            var user = await backgroundUserManager.FindByIdAsync(userId);
            if (user != null)
            {
                user.IncrementLibraryNovelsCount();
                await backgroundUserManager.UpdateAsync(user);
            }
            logger.LogDebug("Successfully incremented library novels count for user {UserId}", userId);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to increment library novels count for user {UserId}", userId);
        }
    }
}
