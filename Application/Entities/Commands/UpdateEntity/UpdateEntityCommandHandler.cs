using Application.Entities.Validators;
using Application.Services;
using Application.Users;
using Application.Users.Commands.FollowUser;
using Domain.Exceptions;
using Domain.Repositories;
using MediatR;
using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace Application.Entities.Commands.UpdateEntity;

public class UpdateEntityCommandHandler(
    ILogger<UpdateEntityCommandHandler> logger,
    INovelEntityRepository entityRepository,
    IUserContext userContext,
    IFileUploadService fileUploadService) : IRequestHandler<UpdateEntityCommand, OperationResult>
{
    public async Task<OperationResult> Handle(UpdateEntityCommand request, CancellationToken cancellationToken)
    {
        var currentUser = userContext.GetCurrentUser() ?? throw new ForbidException("سجّل الدخول للمتابعة", "NotSignedIn");

        var entity = await entityRepository.GetEntityByIdAsync(request.EntityId);
        if (entity == null)
        {
            return new OperationResult
            {
                Success = false,
                Code = "EntityNotFound",
                Message = "هذا المدخل غير موجود"
            };
        }

        // Verify user owns the novel (using included Novel)
        if (entity.Novel == null || entity.Novel.AuthorId != currentUser.Id)
        {
            return new OperationResult { Success = false, Code = "NotOwner", Message = "هذا الإجراء متاح لكاتب الرواية فقط" };
        }

        // Validate icon if provided
        if (request.Icon != null)
        {
            var normalizedIcon = EntityIconValidator.Normalize(request.Icon);
            if (normalizedIcon == null)
            {
                return new OperationResult
                {
                    Success = false,
                    Code = "InvalidIcon",
                    Message = $"الأيقونة غير صالحة. الأيقونات المتاحة: {string.Join("، ", EntityIconValidator.GetValidIcons())}"
                };
            }
            entity.Icon = normalizedIcon;
        }

        // Upload new image if provided
        if (request.ImageFile != null)
        {
            using var stream = request.ImageFile.OpenReadStream();
            entity.ImageUrl = await fileUploadService.UploadEntityGalleryImageAsync(
                stream,
                request.ImageFile.ContentType,
                entity.Id.ToString()
            );
        }

        // Update fields if provided
        if (request.Section != null) entity.Section = request.Section;
        if (request.Name != null) entity.Name = request.Name;
        if (request.ShortDescription != null) entity.ShortDescription = request.ShortDescription;
        if (request.Description != null) entity.Description = request.Description;
        if (request.Role != null) entity.Role = request.Role;
        
        if (!string.IsNullOrEmpty(request.AttributesJson))
        {
            // Validate JSON format
            try
            {
                var testDeserialize = JsonSerializer.Deserialize<Dictionary<string, object>>(request.AttributesJson);
                entity.AttributesJson = request.AttributesJson;
            }
            catch (JsonException)
            {
                return new OperationResult
                {
                    Success = false,
                    Code = "InvalidAttributes",
                    Message = "تعذّرت قراءة السمات. تأكد من صيغتها."
                };
            }
        }

        entity.UpdatedAt = DateTime.UtcNow;

        await entityRepository.UpdateEntityAsync(entity);

        logger.LogInformation("Entity {EntityId} updated successfully", entity.Id);

        return new OperationResult
        {
            Success = true,
            Message = "حُفظ المدخل"
        };
    }
}
