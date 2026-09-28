using Application.Services;
using Application.Users;
using Application.Users.Commands.FollowUser;
using Domain.Entities;
using Domain.Exceptions;
using Domain.Repositories;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Application.Entities.Commands.AddGalleryImage;

public class AddGalleryImageCommandHandler(
    ILogger<AddGalleryImageCommandHandler> logger,
    INovelEntityRepository entityRepository,
    IUserContext userContext,
    IFileUploadService fileUploadService) : IRequestHandler<AddGalleryImageCommand, OperationResult>
{
    public async Task<OperationResult> Handle(AddGalleryImageCommand request, CancellationToken cancellationToken)
    {
        var currentUser = userContext.GetCurrentUser() ?? throw new ForbidException("سجّل الدخول للمتابعة", "NotSignedIn");

        var entity = await entityRepository.GetEntityByIdAsync(request.EntityId);
        if (entity == null)
        {
            return new OperationResult { Success = false, Code = "EntityNotFound", Message = "هذا المدخل غير موجود" };
        }

        // Verify user owns the novel (using included Novel)
        if (entity.Novel == null || entity.Novel.AuthorId != currentUser.Id)
        {
            return new OperationResult { Success = false, Code = "NotOwner", Message = "هذا الإجراء متاح لكاتب الرواية فقط" };
        }

        // Upload image to Cloudflare R2
        using var stream = request.ImageFile.OpenReadStream();
        var imageUrl = await fileUploadService.UploadEntityGalleryImageAsync(
            stream,
            request.ImageFile.ContentType,
            entity.Id.ToString()
        );

        var galleryImage = new EntityGalleryImage
        {
            Id = Guid.NewGuid(),
            EntityId = request.EntityId,
            ImageUrl = imageUrl,
            Caption = request.Caption,
            OrderIndex = request.OrderIndex,
            CreatedAt = DateTime.UtcNow
        };

        await entityRepository.AddGalleryImageAsync(galleryImage);

        logger.LogInformation("Gallery image added to entity {EntityId}", entity.Id);

        return new OperationResult { Success = true, Message = "أُضيفت الصورة إلى معرض الصور" };
    }
}
