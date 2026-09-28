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
    IUserContext userContext) : IRequestHandler<RemoveGalleryImageCommand, OperationResult>
{
    public async Task<OperationResult> Handle(RemoveGalleryImageCommand request, CancellationToken cancellationToken)
    {
        var currentUser = userContext.GetCurrentUser() ?? throw new ForbidException("سجّل الدخول للمتابعة", "NotSignedIn");

        // Get the image directly by ID
        var targetImage = await entityRepository.GetGalleryImageByIdAsync(request.ImageId);
        
        if (targetImage == null)
        {
            return new OperationResult { Success = false, Code = "GalleryImageNotFound", Message = "الصورة غير موجودة في معرض الصور" };
        }

        var entity = await entityRepository.GetEntityByIdAsync(targetImage.EntityId);
        if (entity == null)
        {
            return new OperationResult { Success = false, Code = "EntityNotFound", Message = "هذا المدخل غير موجود" };
        }

        var novel = await novelsRepository.GetOne(entity.NovelId);
        if (novel == null || novel.AuthorId != currentUser.Id)
        {
            return new OperationResult { Success = false, Code = "NotOwner", Message = "هذا الإجراء متاح لكاتب الرواية فقط" };
        }

        await entityRepository.DeleteGalleryImageAsync(request.ImageId);

        logger.LogInformation("Gallery image {ImageId} removed from entity {EntityId}", request.ImageId, entity.Id);

        return new OperationResult { Success = true, Message = "حُذفت الصورة من معرض الصور" };
    }
}
