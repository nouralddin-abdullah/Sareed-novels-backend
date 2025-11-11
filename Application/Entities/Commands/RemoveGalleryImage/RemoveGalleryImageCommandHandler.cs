using Application.Services;
using Application.Users;
using Application.Users.Commands.FollowUser;
using Domain.Exceptions;
using Domain.Repositories;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Application.Entities.Commands.RemoveGalleryImage;

public class RemoveGalleryImageCommandHandler(
    ILogger<RemoveGalleryImageCommandHandler> logger,
    INovelEntityRepository entityRepository,
    INovelsRepository novelsRepository,
    IUserContext userContext,
    ISearchIndexQueueService searchQueue) : IRequestHandler<RemoveGalleryImageCommand, OperationResult>
{
    public async Task<OperationResult> Handle(RemoveGalleryImageCommand request, CancellationToken cancellationToken)
    {
        var currentUser = userContext.GetCurrentUser() ?? throw new ForbidException("User not authenticated");

        // Get the image directly by ID
        var targetImage = await entityRepository.GetGalleryImageByIdAsync(request.ImageId);
        
        if (targetImage == null)
        {
            return new OperationResult { Success = false, Message = "Gallery image not found" };
        }

        var entity = await entityRepository.GetEntityByIdAsync(targetImage.EntityId);
        if (entity == null)
        {
            return new OperationResult { Success = false, Message = "Entity not found" };
        }

        var novel = await novelsRepository.GetOne(entity.NovelId);
        if (novel == null || novel.AuthorId != currentUser.Id)
        {
            return new OperationResult { Success = false, Message = "Permission denied" };
        }

        await entityRepository.DeleteGalleryImageAsync(request.ImageId);

        // Queue entity update for Elasticsearch
        await searchQueue.QueueEntityUpdateAsync(entity.Id);

        logger.LogInformation("Gallery image {ImageId} removed from entity {EntityId}", request.ImageId, entity.Id);

        return new OperationResult { Success = true, Message = "Gallery image removed successfully" };
    }
}
