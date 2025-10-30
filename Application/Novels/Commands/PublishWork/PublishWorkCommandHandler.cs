using Application.Users;
using Application.Users.Commands.FollowUser;
using Domain.Exceptions;
using Domain.Repositories;
using MediatR;

namespace Application.Novels.Commands.PublishWork;

public class PublishWorkCommandHandler(INovelsRepository novelsRepository, IUserContext userContext) : IRequestHandler<PublishWorkCommand, OperationResult>
{
    public async Task<OperationResult> Handle(PublishWorkCommand request, CancellationToken cancellationToken)
    {
        var novel = await novelsRepository.GetOne(request.NovelId) ?? throw new NotFoundException("This novel was not found");
        var currentUser = userContext.GetCurrentUser() ?? throw new ForbidException("This user not signed in");
        if (novel.AuthorId != currentUser.Id)
        {
            throw new ForbidException("Forbidden");
        }
        novel.IsDraft = false;
        novel.IsEligibleForRanking = true;
        var result = await novelsRepository.UpdateOne(novel);
        if (result)
        {
            return new OperationResult
            {
                Success = true,
                Message = "Novel has been published successfully"
            };
        }

        return new OperationResult
        {
            Success = false,
            Message = "Novel has not been published"
        };
    }
}
