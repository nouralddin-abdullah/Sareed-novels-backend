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
    IUserContext userContext) : IRequestHandler<CreateRelationshipCommand, OperationResult>
{
    public async Task<OperationResult> Handle(CreateRelationshipCommand request, CancellationToken cancellationToken)
    {
        var currentUser = userContext.GetCurrentUser() ?? throw new ForbidException("سجّل الدخول للمتابعة", "NotSignedIn");

        var sourceEntity = await entityRepository.GetEntityByIdAsync(request.SourceEntityId);
        if (sourceEntity == null)
        {
            return new OperationResult { Success = false, Code = "EntityNotFound", Message = "هذا المدخل غير موجود" };
        }

        var targetEntity = await entityRepository.GetEntityByIdAsync(request.TargetEntityId);
        if (targetEntity == null)
        {
            return new OperationResult { Success = false, Code = "EntityNotFound", Message = "المدخل المرتبط غير موجود" };
        }

        if (sourceEntity.NovelId != targetEntity.NovelId)
        {
            return new OperationResult { Success = false, Code = "EntitiesInDifferentNovels", Message = "يجب أن يكون المدخلان من الرواية نفسها" };
        }

        // Verify user owns the novel (using included Novel from source entity)
        if (sourceEntity.Novel == null || sourceEntity.Novel.AuthorId != currentUser.Id)
        {
            return new OperationResult { Success = false, Code = "NotOwner", Message = "هذا الإجراء متاح لكاتب الرواية فقط" };
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
        

        logger.LogInformation("Relationship created between {Source} and {Target}", sourceEntity.Id, targetEntity.Id);

        return new OperationResult { Success = true, Message = "أُضيفت العلاقة" };
    }
}
