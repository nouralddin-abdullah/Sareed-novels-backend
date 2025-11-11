using Application.Services;
using Application.Users;
using Application.Users.Commands.FollowUser;
using Domain.Exceptions;
using Domain.Repositories;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Application.Entities.Commands.UpdateGalleryImage;

public class UpdateGalleryImageCommandHandler(
    ILogger<UpdateGalleryImageCommandHandler> logger,
    INovelEntityRepository entityRepository,
    IUserContext userContext,
    ISearchIndexQueueService searchQueue) : IRequestHandler<UpdateGalleryImageCommand, OperationResult>
{
    public async Task<OperationResult> Handle(UpdateGalleryImageCommand request, CancellationToken cancellationToken)
    {
        var currentUser = userContext.GetCurrentUser() ?? throw new ForbidException("User not authenticated");

        var image = await entityRepository.GetGalleryImageByIdAsync(request.ImageId);
        if (image == null)
        {
            return new OperationResult { Success = false, Message = "Gallery image not found" };
        }

        var entity = await entityRepository.GetEntityByIdAsync(image.EntityId);
        if (entity == null)
        {
            return new OperationResult { Success = false, Message = "Entity not found" };
        }

        // Verify user owns the novel (using included Novel)
        if (entity.Novel == null || entity.Novel.AuthorId != currentUser.Id)
        {
            return new OperationResult { Success = false, Message = "Permission denied" };
        }

        // Update caption if provided
        if (request.Caption != null)
        {
            image.Caption = request.Caption;
        }

        await entityRepository.UpdateGalleryImageAsync(image);

        // Queue entity update for Elasticsearch
        await searchQueue.QueueEntityUpdateAsync(entity.Id);

        logger.LogInformation("Gallery image {ImageId} caption updated", image.Id);

        return new OperationResult { Success = true, Message = "Gallery image updated successfully" };
    }
}
