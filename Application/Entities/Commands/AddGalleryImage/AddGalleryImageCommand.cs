using Application.Users.Commands.FollowUser;
using MediatR;
using Microsoft.AspNetCore.Http;

namespace Application.Entities.Commands.AddGalleryImage;

public class AddGalleryImageCommand : IRequest<OperationResult>
{
    public Guid EntityId { get; set; }
    public IFormFile ImageFile { get; set; } = default!;
    public string? Caption { get; set; }
    public int OrderIndex { get; set; }
}
