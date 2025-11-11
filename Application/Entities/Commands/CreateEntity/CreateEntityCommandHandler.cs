using Application.Entities.Validators;
using Application.Services;
using Application.Users;
using Application.Users.Commands.FollowUser;
using Domain.Entities;
using Domain.Exceptions;
using Domain.Repositories;
using MediatR;
using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace Application.Entities.Commands.CreateEntity;

public class CreateEntityCommandHandler(
    ILogger<CreateEntityCommandHandler> logger,
    INovelEntityRepository entityRepository,
    INovelsRepository novelsRepository,
    IUserContext userContext,
    ISearchIndexQueueService searchQueue,
    IFileUploadService fileUploadService) : IRequestHandler<CreateEntityCommand, OperationResult>
{
    public async Task<OperationResult> Handle(CreateEntityCommand request, CancellationToken cancellationToken)
    {
        var currentUser = userContext.GetCurrentUser() ?? throw new ForbidException("User not authenticated");

        logger.LogInformation(
            "User {UserId} creating entity '{Name}' for novel {NovelId}",
            currentUser.Id,
            request.Name,
            request.NovelId
        );

        // Verify novel exists and user is the author
        var novel = await novelsRepository.GetOne(request.NovelId);
        if (novel == null)
        {
            return new OperationResult
            {
                Success = false,
                Message = "Novel not found"
            };
        }

        if (novel.AuthorId != currentUser.Id)
        {
            return new OperationResult
            {
                Success = false,
                Message = "You don't have permission to add entities to this novel"
            };
        }

        // Validate and normalize icon
        var normalizedIcon = EntityIconValidator.Normalize(request.Icon);
        if (request.Icon != null && normalizedIcon == null)
        {
            return new OperationResult
            {
                Success = false,
                Message = $"Invalid icon. Valid icons are: {string.Join(", ", EntityIconValidator.GetValidIcons())}"
            };
        }

        // Upload image if provided
        string? imageUrl = null;
        if (request.ImageFile != null)
        {
            using var stream = request.ImageFile.OpenReadStream();
            imageUrl = await fileUploadService.UploadEntityGalleryImageAsync(
                stream,
                request.ImageFile.ContentType,
                Guid.NewGuid().ToString() // Temporary ID, will use entity.Id after creation
            );
        }

        var entity = new NovelEntity
        {
            Id = Guid.NewGuid(),
            NovelId = request.NovelId,
            Section = request.Section,
            Icon = normalizedIcon,
            Name = request.Name,
            ShortDescription = request.ShortDescription,
            Description = request.Description,
            Role = request.Role,
            ImageUrl = imageUrl,
            AttributesJson = JsonSerializer.Serialize(request.Attributes),
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        await entityRepository.CreateEntityAsync(entity);

        // Queue for Elasticsearch indexing
        await searchQueue.QueueEntityIndexAsync(entity.Id);

        logger.LogInformation("Entity {EntityId} created successfully", entity.Id);

        return new OperationResult
        {
            Success = true,
            Message = "Entity created successfully"
        };
    }
}
