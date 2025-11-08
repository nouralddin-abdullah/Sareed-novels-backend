using Application.Services;
using Application.Users;
using Application.Users.Commands.FollowUser;
using Domain.Exceptions;
using Domain.Repositories;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Application.Library.Commands.RecalculateSequences;

public class RecalculateChapterSequencesCommandHandler(
    ILogger<RecalculateChapterSequencesCommandHandler> logger,
    IChapterSequenceService sequenceService,
    INovelsRepository novelsRepository,
    IUserContext userContext) : IRequestHandler<RecalculateChapterSequencesCommand, OperationResult>
{
    public async Task<OperationResult> Handle(RecalculateChapterSequencesCommand request, CancellationToken cancellationToken)
    {
        var currentUser = userContext.GetCurrentUser() ?? throw new ForbidException("User not signed in");
        
        var novel = await novelsRepository.GetOne(request.NovelId) 
            ?? throw new NotFoundException("Novel not found");
        
        if (novel.AuthorId != currentUser.Id)
        {
            throw new ForbidException("Only the author can recalculate chapter sequences");
        }
        
        logger.LogInformation(
            "User {UserId} manually triggering sequence recalculation for novel {NovelId}", 
            currentUser.Id, request.NovelId);
        
        await sequenceService.RecalculateSequencesForNovelAsync(request.NovelId);
        await sequenceService.UpdateReadingProgressForNovelAsync(request.NovelId);
        
        return new OperationResult
        {
            Success = true,
            Message = "Chapter sequences recalculated successfully"
        };
    }
}
