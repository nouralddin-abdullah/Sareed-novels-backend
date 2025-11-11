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
    ISearchIndexQueueService searchQueue,
    IFileUploadService fileUploadService) : IRequestHandler<UpdateEntityCommand, OperationResult>
{
    public async Task<OperationResult> Handle(UpdateEntityCommand request, CancellationToken cancellationToken)
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

        // Verify user owns the novel (using included Novel)
        if (entity.Novel == null || entity.Novel.AuthorId != currentUser.Id)
        {
            return new OperationResult { Success = false, Message = "Permission denied" };
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
                    Message = $"Invalid icon. Valid icons are: {string.Join(", ", EntityIconValidator.GetValidIcons())}"
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
                    Message = "Invalid JSON format for Attributes"
                };
            }
        }

        entity.UpdatedAt = DateTime.UtcNow;

        await entityRepository.UpdateEntityAsync(entity);

        // Queue for Elasticsearch update
        await searchQueue.QueueEntityUpdateAsync(entity.Id);

        logger.LogInformation("Entity {EntityId} updated successfully", entity.Id);

        return new OperationResult
        {
            Success = true,
            Message = "Entity updated successfully"
        };
    }
}
