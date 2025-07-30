using Application.Users;
using Application.Users.Commands.FollowUser;
using AutoMapper;
using Domain.Exceptions;
using Domain.Repositories;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Application.Novels.Commands.UpdateNovel;

public class UpdateNovelCommandHandler(ILogger<UpdateNovelCommandHandler> logger, IUserContext userContext, INovelsRepository novelsRepository, IMapper mapper) : IRequestHandler<UpdateNovelCommand, OperationResult>
{
    public async Task<OperationResult> Handle(UpdateNovelCommand request, CancellationToken cancellationToken)
    {
        var novel = await novelsRepository.GetOne(request.NovelId) ?? throw new NotFoundException("This novel was not found");
        var currentUser = userContext.GetCurrentUser() ?? throw new ForbidException("This user not signed in");
        logger.LogInformation("Updating data for novel {@novel}", novel);
        if (novel.AuthorId != currentUser.Id)
        {
            throw new ForbidException("Forbidden");
        }
        mapper.Map(request, novel);
        bool result = await novelsRepository.UpdateOne(novel);
        if (result)
        {
            return new OperationResult
            {
                Message = "Novel was updated successfully",
                Success = true

            };
        }
        return new OperationResult
        {
            Message = "Novel wasn't updated successfully",
            Success = false

        };
    }
}
