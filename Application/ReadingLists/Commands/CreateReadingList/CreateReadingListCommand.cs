using Application.Users.Commands.FollowUser;
using MediatR;
using Microsoft.AspNetCore.Http;

namespace Application.ReadingLists.Commands.CreateReadingList;

public class CreateReadingListCommand : IRequest<OperationResult>
{
    public string Name { get; set; } = default!;
    public string? Description { get; set; }
    public IFormFile? CoverImage { get; set; }
    public bool IsPublic { get; set; } = false;
}
