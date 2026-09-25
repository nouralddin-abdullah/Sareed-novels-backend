using Application.Library.DTOs;
using Application.Users;
using Domain.Library;
using Domain.Repositories;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Application.Library.Queries.GetNovelProgress;

public class GetNovelProgressQueryHandler(
    ILogger<GetNovelProgressQueryHandler> logger,
    ILibraryRepository libraryRepository,
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

        var entry = await libraryRepository.GetLibraryEntryAsync(currentUser.Id, request.NovelId);

        if (entry == null)
        {
            return null;
        }

        var resume = ReadingPosition.Resolve(entry.LastReadChapter, entry.PublishedChapters);
        if (resume.ChapterId != entry.LastReadChapter.Id)
        {
            logger.LogInformation(
                "Chapter {ChapterId} that user {UserId} last read in novel {NovelId} is no longer published; resuming at {ResumeChapterId}",
                entry.LastReadChapter.Id, currentUser.Id, request.NovelId, resume.ChapterId);
        }

        return new NovelProgressDTO
        {
            NovelId = entry.NovelId,
            LastReadChapterId = resume.ChapterId,
            LastReadChapterNumber = resume.ChapterNumber,
            ProgressPercentage = resume.ProgressPercentage,
            LastReadAt = entry.LastReadAt
        };
    }
}
