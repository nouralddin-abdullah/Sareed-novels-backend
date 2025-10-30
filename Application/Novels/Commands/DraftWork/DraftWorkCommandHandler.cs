using Application.Users;
using Application.Users.Commands.FollowUser;
using Domain.Exceptions;
using Domain.Repositories;
using MediatR;

namespace Application.Novels.Commands.DraftWork;

public class DraftWorkCommandHandler(INovelsRepository novelsRepository, IUserContext userContext) : IRequestHandler<DraftWorkCommand, OperationResult>
{
    public async Task<OperationResult> Handle(DraftWorkCommand request, CancellationToken cancellationToken)
    {
        var novel = await novelsRepository.GetOne(request.NovelId) ?? throw new NotFoundException("This novel was not found");
        var currentUser = userContext.GetCurrentUser() ?? throw new ForbidException("This user not signed in");
        if (novel.AuthorId != currentUser.Id)
        {
            throw new ForbidException("Forbidden");
        }
        novel.IsDraft = true;
        novel.IsEligibleForRanking = false;
        var result = await novelsRepository.UpdateOne(novel);
        if (result)
        {
            return new OperationResult
            {
                Success = true,
                Message = "Novel has been drafted successfully"
            };
        }

        return new OperationResult
        {
            Success = false,
            Message = "Novel has not been drafted"
        };
    }
}
