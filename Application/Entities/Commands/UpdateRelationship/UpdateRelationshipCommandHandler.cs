using Application.Services;
using Application.Users;
using Application.Users.Commands.FollowUser;
using Domain.Exceptions;
using Domain.Repositories;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Application.Entities.Commands.UpdateRelationship;

public class UpdateRelationshipCommandHandler(
    ILogger<UpdateRelationshipCommandHandler> logger,
    INovelEntityRepository entityRepository,
    IUserContext userContext,
    ISearchIndexQueueService searchQueue) : IRequestHandler<UpdateRelationshipCommand, OperationResult>
{
    public async Task<OperationResult> Handle(UpdateRelationshipCommand request, CancellationToken cancellationToken)
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

        // Verify user owns the novel (using included Novel)
        if (sourceEntity.Novel == null || sourceEntity.Novel.AuthorId != currentUser.Id)
        {
            return new OperationResult { Success = false, Message = "Permission denied" };
        }

        // Update only provided fields
        if (request.RelationType != null) relationship.RelationType = request.RelationType;
        if (request.Label != null) relationship.Label = request.Label;
        if (request.ReverseLabel != null) relationship.ReverseLabel = request.ReverseLabel;
        if (request.Description != null) relationship.Description = request.Description;

        await entityRepository.UpdateRelationshipAsync(relationship);

        // Queue both entities for update (relationship affects both)
        await searchQueue.QueueEntityUpdateAsync(relationship.SourceEntityId);
        await searchQueue.QueueEntityUpdateAsync(relationship.TargetEntityId);

        logger.LogInformation("Relationship {RelationshipId} updated", relationship.Id);

        return new OperationResult { Success = true, Message = "Relationship updated successfully" };
    }
}
