using Application.Users.Commands.FollowUser;
using MediatR;

namespace Application.Entities.Commands.RemoveGalleryImage;

public class RemoveGalleryImageCommand(Guid imageId) : IRequest<OperationResult>
{
    public Guid ImageId { get; set; } = imageId;
}
