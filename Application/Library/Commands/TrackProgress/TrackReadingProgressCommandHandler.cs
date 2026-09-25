using Application.Users;
using Application.Users.Commands.FollowUser;
using Domain.Exceptions;
using Domain.Repositories;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Application.Library.Commands.TrackProgress;

public class TrackReadingProgressCommandHandler(
    ILogger<TrackReadingProgressCommandHandler> logger,
    ILibraryRepository libraryRepository,
    IChaptersRepository chaptersRepository,
    INovelsRepository novelsRepository,
    IUserContext userContext) : IRequestHandler<TrackReadingProgressCommand, OperationResult>
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

        // Readers can't open chapters of draft or deleted novels (GetOne applies the soft-delete filter), and the
        // library doesn't list them, so don't record progress in them either.
        var novel = await novelsRepository.GetOne(chapter.NovelId);
        if (novel == null || !novel.IsPubliclyVisible)
        {
            return new OperationResult
            {
                Success = false,
                Message = "Cannot track progress for unpublished novels"
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

        // "Stopped at" is the chapter the reader opened most recently, so re-reading an earlier chapter moves it back.
        var addedToLibrary = await libraryRepository.SaveProgressAsync(
            currentUser.Id, chapter.NovelId, chapter.Id, publishedSequenceNumber.Value, DateTime.UtcNow);

        logger.LogInformation(
            addedToLibrary
                ? "Created new reading progress for user {UserId}, novel {NovelId}"
                : "Updated reading progress for user {UserId}, novel {NovelId}",
            currentUser.Id, chapter.NovelId);

        return new OperationResult
        {
            Success = true,
            Message = "Reading progress tracked successfully"
        };
    }
}
