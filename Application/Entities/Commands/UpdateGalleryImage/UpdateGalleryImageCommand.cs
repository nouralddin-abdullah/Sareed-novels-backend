using Application.Users.Commands.FollowUser;
using MediatR;

namespace Application.Entities.Commands.UpdateGalleryImage;

public class UpdateGalleryImageCommand : IRequest<OperationResult>
{
    public Guid ImageId { get; set; }
    public string? Caption { get; set; }
}
