using Application.Library.DTOs;
using Application.Users;
using Domain.Repositories;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Application.Library.Queries.GetNovelProgress;

public class GetNovelProgressQueryHandler(
    ILogger<GetNovelProgressQueryHandler> logger,
    ILibraryRepository libraryRepository,
    IChaptersRepository chaptersRepository,
    IUserContext userContext) : IRequestHandler<GetNovelProgressQuery, NovelProgressDTO?>
{
    public async Task<NovelProgressDTO?> Handle(GetNovelProgressQuery request, CancellationToken cancellationToken)
    {
        var currentUser = userContext.GetCurrentUser();
        
        if (currentUser == null)
        {
            return null;
        }

        logger.LogInformation("Getting progress for novel {NovelId}, user {UserId}", request.NovelId, currentUser.Id);

        var progress = await libraryRepository.GetProgressAsync(currentUser.Id, request.NovelId);

        if (progress == null)
        {
            return null;
        }

        var publishedChapters = await chaptersRepository.GetChaptersReaderView(request.NovelId);
        var publishedChaptersList = publishedChapters.OrderBy(c => c.ChapterIndex).ToList();
        var publishedCount = publishedChaptersList.Count;
        
        var currentSequence = publishedChaptersList.FindIndex(c => c.Id == progress.LastReadChapterId) + 1;
        
        if (currentSequence == 0)
        {
            logger.LogWarning("User {UserId} has progress for deleted/unpublished chapter {ChapterId} in novel {NovelId}", 
                currentUser.Id, progress.LastReadChapterId, request.NovelId);
            currentSequence = progress.LastReadChapterNumber;
        }

        var dto = new NovelProgressDTO
        {
            NovelId = progress.NovelId,
            LastReadChapterId = progress.LastReadChapterId,
            LastReadChapterNumber = currentSequence,
            ProgressPercentage = publishedCount > 0
                ? Math.Round((decimal)currentSequence / publishedCount * 100, 1)
                : 0,
            LastReadAt = progress.LastReadAt
        };

        return dto;
    }
}
