using Application.Users;
using Domain.Exceptions;
using Domain.Repositories;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Application.Entities.Commands.ReorderGalleryImages;

public class ReorderGalleryImagesCommandHandler(
    ILogger<ReorderGalleryImagesCommandHandler> logger,
    INovelEntityRepository entityRepository,
    INovelsRepository novelsRepository,
    IUserContext userContext) : IRequestHandler<ReorderGalleryImagesCommand, bool>
{
    public async Task<bool> Handle(ReorderGalleryImagesCommand request, CancellationToken cancellationToken)
    {
        logger.LogInformation("Reordering gallery images for entity {EntityId}", request.EntityId);
        
        var currentUser = userContext.GetCurrentUser() ?? throw new ForbidException("User not authenticated");
        
        var entity = await entityRepository.GetEntityByIdAsync(request.EntityId);
        if (entity == null)
        {
            logger.LogWarning("Entity {EntityId} not found", request.EntityId);
            return false;
        }
        
        var novel = await novelsRepository.GetOne(entity.NovelId);
        if (novel == null || novel.AuthorId != currentUser.Id)
        {
            throw new ForbidException("User doesn't own this novel");
        }
        
        var result = await entityRepository.ReorderGalleryImagesAsync(request.EntityId, request.OrderedImageIds);
        
        if (result)
        {
            logger.LogInformation("Gallery images reordered successfully for entity {EntityId}", request.EntityId);
        }
        else
        {
            logger.LogWarning("Failed to reorder gallery images for entity {EntityId}", request.EntityId);
        }
        
        return result;
    }
}
