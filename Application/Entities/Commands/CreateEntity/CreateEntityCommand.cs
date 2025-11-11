using Application.Users.Commands.FollowUser;
using MediatR;
using Microsoft.AspNetCore.Http;

namespace Application.Entities.Commands.CreateEntity;

public class CreateEntityCommand : IRequest<OperationResult>
{
    public Guid NovelId { get; set; }
    public string Section { get; set; } = default!;
    public string? Icon { get; set; }
    public string Name { get; set; } = default!;
    public string? ShortDescription { get; set; }
    public string? Description { get; set; }
    public string? Role { get; set; }
    public IFormFile? ImageFile { get; set; }  // Changed from ImageUrl to ImageFile
    public Dictionary<string, object> Attributes { get; set; } = new();
}
