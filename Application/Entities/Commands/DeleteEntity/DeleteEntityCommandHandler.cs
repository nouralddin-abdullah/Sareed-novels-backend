using Application.Services;
using Application.Users;
using Application.Users.Commands.FollowUser;
using Domain.Exceptions;
using Domain.Repositories;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Application.Entities.Commands.DeleteEntity;

public class DeleteEntityCommandHandler(
    ILogger<DeleteEntityCommandHandler> logger,
    INovelEntityRepository entityRepository,
    INovelsRepository novelsRepository,
    IUserContext userContext,
    ISearchIndexQueueService searchQueue) : IRequestHandler<DeleteEntityCommand, OperationResult>
{
    public async Task<OperationResult> Handle(DeleteEntityCommand request, CancellationToken cancellationToken)
    {
        var currentUser = userContext.GetCurrentUser() ?? throw new ForbidException("User not authenticated");

        var entity = await entityRepository.GetEntityByIdAsync(request.EntityId);
        if (entity == null)
        {
            return new OperationResult
            {
                Success = false,
                Message = "Entity not found"
            };
        }

        // Verify user owns the novel
        var novel = await novelsRepository.GetOne(entity.NovelId);
        if (novel == null || novel.AuthorId != currentUser.Id)
        {
            return new OperationResult
            {
                Success = false,
                Message = "You don't have permission to delete this entity"
            };
        }

        await entityRepository.DeleteEntityAsync(request.EntityId);

        // Queue for Elasticsearch deletion
        await searchQueue.QueueEntityDeleteAsync(request.EntityId);

        logger.LogInformation("Entity {EntityId} deleted successfully", request.EntityId);

        return new OperationResult
        {
            Success = true,
            Message = "Entity deleted successfully"
        };
    }
}
