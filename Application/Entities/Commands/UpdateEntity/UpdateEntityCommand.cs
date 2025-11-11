using Application.Users.Commands.FollowUser;
using MediatR;
using Microsoft.AspNetCore.Http;

namespace Application.Entities.Commands.UpdateEntity;

public class UpdateEntityCommand : IRequest<OperationResult>
{
    public Guid EntityId { get; set; }
    public string? Section { get; set; }
    public string? Icon { get; set; }
    public string? Name { get; set; }
    public string? ShortDescription { get; set; }
    public string? Description { get; set; }
    public string? Role { get; set; }
    public IFormFile? ImageFile { get; set; }
    
    /// <summary>
    /// JSON string of attributes. Frontend should send: JSON.stringify({key: value, ...})
    /// </summary>
    public string? AttributesJson { get; set; }
}
