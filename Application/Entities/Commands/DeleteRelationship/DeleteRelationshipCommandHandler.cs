using Application.Services;
using Application.Users;
using Application.Users.Commands.FollowUser;
using Domain.Exceptions;
using Domain.Repositories;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Application.Entities.Commands.DeleteRelationship;

public class DeleteRelationshipCommandHandler(
    ILogger<DeleteRelationshipCommandHandler> logger,
    INovelEntityRepository entityRepository,
    INovelsRepository novelsRepository,
    IUserContext userContext,
    ISearchIndexQueueService searchQueue) : IRequestHandler<DeleteRelationshipCommand, OperationResult>
{
    public async Task<OperationResult> Handle(DeleteRelationshipCommand request, CancellationToken cancellationToken)
    {
        var currentUser = userContext.GetCurrentUser() ?? throw new ForbidException("User not authenticated");

        var relationship = await entityRepository.GetRelationshipByIdAsync(request.RelationshipId);
        if (relationship == null)
        {
            return new OperationResult { Success = false, Message = "Relationship not found" };
        }

        var sourceEntity = await entityRepository.GetEntityByIdAsync(relationship.SourceEntityId);
        if (sourceEntity == null)
        {
            return new OperationResult { Success = false, Message = "Source entity not found" };
        }

        var novel = await novelsRepository.GetOne(sourceEntity.NovelId);
        if (novel == null || novel.AuthorId != currentUser.Id)
        {
            return new OperationResult { Success = false, Message = "Permission denied" };
        }

        await entityRepository.DeleteRelationshipAsync(request.RelationshipId);

        // Queue both entities for update
        await searchQueue.QueueEntityUpdateAsync(relationship.SourceEntityId);
        await searchQueue.QueueEntityUpdateAsync(relationship.TargetEntityId);

        logger.LogInformation("Relationship {RelationshipId} deleted", request.RelationshipId);

        return new OperationResult { Success = true, Message = "Relationship deleted successfully" };
    }
}
