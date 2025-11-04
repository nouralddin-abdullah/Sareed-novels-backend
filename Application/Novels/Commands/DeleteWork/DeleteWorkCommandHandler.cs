using Application.Novels.Commands.DraftWork;
using Application.Users;
using Application.Users.Commands.FollowUser;
using Domain.Exceptions;
using Domain.Repositories;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Application.Novels.Commands.DeleteWork;

public class DeleteWorkCommandHandler(
    ILogger<DeleteWorkCommandHandler> logger,
    INovelsRepository novelsRepository, 
    IReadingListNovelsRepository readingListNovelsRepository,
    IUserContext userContext) : IRequestHandler<DeleteWorkCommand, OperationResult>
{
    public async Task<OperationResult> Handle(DeleteWorkCommand request, CancellationToken cancellationToken)
    {
        var novel = await novelsRepository.GetOne(request.NovelId) ?? throw new NotFoundException("This novel was not found");
        var currentUser = userContext.GetCurrentUser() ?? throw new ForbidException("This user not signed in");
        
        if (novel.AuthorId != currentUser.Id)
        {
            throw new ForbidException("Forbidden");
        }
        
        novel.IsDeleted = true;
        novel.IsEligibleForRanking = false;
        
        var result = await novelsRepository.UpdateOne(novel);
        
        if (result)
        {
            // Note: We don't need to manually remove from reading lists
            // The ReadingListNovelsRepository uses IgnoreQueryFilters for internal checks
            // and filters deleted novels in GetNovelsInListAsync
            // Orphaned entries will be cleaned up lazily when lists are accessed
            
            logger.LogInformation("Novel {NovelId} soft-deleted by user {UserId}", request.NovelId, currentUser.Id);
            
            return new OperationResult
            {
                Success = true,
                Message = "Novel has been deleted successfully"
            };
        }

        return new OperationResult
        {
            Success = false,
            Message = "Novel has not been deleted"
        };
    }
}
