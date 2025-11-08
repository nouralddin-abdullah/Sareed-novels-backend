using Application.Chapters.Commands.CreateChapter;
using Application.Services;
using Application.Users;
using Application.Users.Commands.FollowUser;
using AutoMapper;
using Domain.Exceptions;
using Domain.Repositories;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Application.Chapters.Commands.ReorderChapter;

internal class ReorderChaptersHandler(
    ILogger<ReorderChaptersHandler> logger,
    IChaptersRepository chaptersRepository,
    IUserContext userContext,
    INovelsRepository novelsRepository,
    IChapterSequenceService sequenceService) : IRequestHandler<ReorderChaptersCommand, bool>
{
    public async Task<bool> Handle(ReorderChaptersCommand request, CancellationToken cancellationToken)
    {
        logger.LogInformation("Reordering chapters {@chapter}", request);
        var currentUser = userContext.GetCurrentUser() ?? throw new ForbidException("User not signed in");
        var novel = await novelsRepository.GetOne(request.NovelId) ?? throw new NotFoundException("This novel wasn't found");
        if (novel.AuthorId != currentUser.Id) throw new ForbidException("User doesn't own this novel");

        var result = await chaptersRepository.ReorderChapters(request.NovelId, request.OrderedChapterIds);

        if (result)
        {
            logger.LogInformation(
                "Chapters reordered for novel {NovelId}, triggering sequence recalculation",
                request.NovelId);

            // Reordering changes ChapterIndex, which affects PublishedChapterSequence
            await sequenceService.RecalculateSequencesForNovelAsync(request.NovelId);
            await sequenceService.UpdateReadingProgressForNovelAsync(request.NovelId);
        }

        return result;
    }
}
