using Application.Services;
using Application.Users;
using Application.Users.Commands.FollowUser;
using Domain.Entities;
using Domain.Exceptions;
using Domain.Repositories;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Application.Entities.Commands.CreateRelationship;

public class CreateRelationshipCommandHandler(
    ILogger<CreateRelationshipCommandHandler> logger,
    INovelEntityRepository entityRepository,
    IUserContext userContext,
    ISearchIndexQueueService searchQueue) : IRequestHandler<CreateRelationshipCommand, OperationResult>
{
    public async Task<OperationResult> Handle(CreateRelationshipCommand request, CancellationToken cancellationToken)
    {
        var currentUser = userContext.GetCurrentUser() ?? throw new ForbidException("User not authenticated");

        var sourceEntity = await entityRepository.GetEntityByIdAsync(request.SourceEntityId);
        if (sourceEntity == null)
        {
            return new OperationResult { Success = false, Message = "Source entity not found" };
        }

        var targetEntity = await entityRepository.GetEntityByIdAsync(request.TargetEntityId);
        if (targetEntity == null)
        {
            return new OperationResult { Success = false, Message = "Target entity not found" };
        }

        if (sourceEntity.NovelId != targetEntity.NovelId)
        {
            return new OperationResult { Success = false, Message = "Entities must belong to the same novel" };
        }

        // Verify user owns the novel (using included Novel from source entity)
        if (sourceEntity.Novel == null || sourceEntity.Novel.AuthorId != currentUser.Id)
        {
            return new OperationResult { Success = false, Message = "Permission denied" };
        }

        var relationship = new EntityRelationship
        {
            Id = Guid.NewGuid(),
            SourceEntityId = request.SourceEntityId,
            TargetEntityId = request.TargetEntityId,
            RelationType = request.RelationType,
            Label = request.Label,
            ReverseLabel = request.ReverseLabel,
            Description = request.Description,
            CreatedAt = DateTime.UtcNow
        };

        await entityRepository.CreateRelationshipAsync(relationship);
        await searchQueue.QueueEntityUpdateAsync(sourceEntity.Id);
        
        // If reverse label exists, also update target entity in search index
        if (!string.IsNullOrEmpty(request.ReverseLabel))
        {
            await searchQueue.QueueEntityUpdateAsync(targetEntity.Id);
        }

        logger.LogInformation("Relationship created between {Source} and {Target}", sourceEntity.Id, targetEntity.Id);

        return new OperationResult { Success = true, Message = "Relationship created successfully" };
    }
}
