using Application.Common;
using Application.Library.DTOs;
using Application.Users;
using Domain.Exceptions;
using Domain.Repositories;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Application.Library.Queries.GetMyLibrary;

public class GetMyLibraryQueryHandler(
    ILogger<GetMyLibraryQueryHandler> logger,
    ILibraryRepository libraryRepository,
    IUserContext userContext) : IRequestHandler<GetMyLibraryQuery, PagedResult<ReadingProgressDTO>>
{
    public async Task<PagedResult<ReadingProgressDTO>> Handle(GetMyLibraryQuery request, CancellationToken cancellationToken)
    {
        var currentUser = userContext.GetCurrentUser() ?? throw new ForbidException("User not signed in");
        logger.LogInformation("Getting library for user {UserId}, page {Page}", currentUser.Id, request.PageNumber);

        var (progressList, totalCount) = await libraryRepository.GetUserReadingProgressAsync(
            currentUser.Id,
            request.PageNumber,
            request.PageSize
        );

        var dtos = progressList.Select(progress =>
        {
            var publishedChapters = progress.Novel.Chapters
                .Where(c => c.Status == "Published")
                .OrderBy(c => c.ChapterIndex)
                .ToList();
            
            var publishedCount = publishedChapters.Count;
            
            // Use the cached PublishedChapterSequence from LastReadChapter
            var currentSequence = progress.LastReadChapter.PublishedChapterSequence ?? 0;
            
            // Fallback: If chapter has no cached sequence (shouldn't happen), recalculate
            if (currentSequence == 0)
            {
                logger.LogWarning(
                    "LastReadChapter {ChapterId} for user {UserId} has no PublishedChapterSequence, falling back to calculation", 
                    progress.LastReadChapterId, currentUser.Id);
                
                currentSequence = publishedChapters.FindIndex(c => c.Id == progress.LastReadChapterId) + 1;
                
                // If chapter was deleted or unpublished
                if (currentSequence == 0)
                {
                    logger.LogWarning(
                        "User {UserId} has progress for deleted/unpublished chapter {ChapterId} in novel {NovelId}", 
                        currentUser.Id, progress.LastReadChapterId, progress.NovelId);
                    
                    currentSequence = progress.LastReadChapterNumber;
                }
            }
            
            return new ReadingProgressDTO
            {
                NovelId = progress.NovelId,
                Title = progress.Novel.Title,
                Slug = progress.Novel.Slug,
                CoverImageUrl = progress.Novel.CoverImageUrl,
                TotalChapters = publishedCount,
                TotalAverageScore = progress.Novel.TotalAverageScore,
                TotalViews = progress.Novel.TotalViews,
                LastReadChapterId = progress.LastReadChapterId,
                LastReadChapterNumber = currentSequence,
                LastReadChapterTitle = progress.LastReadChapter.Title,
                ProgressPercentage = publishedCount > 0
                    ? Math.Round((decimal)currentSequence / publishedCount * 100, 1)
                    : 0,
                LastReadAt = progress.LastReadAt,
                Author = new NovelAuthorDTO
                {
                    UserName = progress.Novel.Owner.UserName!,
                    DisplayName = progress.Novel.Owner.DisplayName,
                    ProfilePhoto = progress.Novel.Owner.ProfilePhoto
                }
            };
        }).ToList();

        return new PagedResult<ReadingProgressDTO>(
            dtos,
            totalCount,
            request.PageSize,
            request.PageNumber
        );
    }
}
