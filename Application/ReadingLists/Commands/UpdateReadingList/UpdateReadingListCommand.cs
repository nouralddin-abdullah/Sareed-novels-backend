using Application.Users.Commands.FollowUser;
using MediatR;
using Microsoft.AspNetCore.Http;

namespace Application.ReadingLists.Commands.UpdateReadingList;

public class UpdateReadingListCommand(Guid readingListId, string? name, string? description, bool? isPublic, IFormFile? coverImage) : IRequest<OperationResult>
{
    public Guid ReadingListId { get; set; } = readingListId;
    public string? Name { get; set; } = name;
    public string? Description { get; set; } = description;
    public bool? IsPublic { get; set; } = isPublic;
    public IFormFile? CoverImage { get; set; } = coverImage;
}
