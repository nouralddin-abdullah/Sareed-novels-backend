using Application.Users.Commands.FollowUser;
using MediatR;
using Microsoft.AspNetCore.Http;

namespace Application.ReadingLists.Commands.UpdateReadingList;

/// <summary>
/// An edit of a reading list by its owner. Null leaves a field as it is; a blank <see cref="Description"/> clears it;
/// <see cref="RemoveCover"/> removes the picture (not together with a new <see cref="CoverImage"/>).
/// </summary>
public class UpdateReadingListCommand(Guid readingListId, string? name, string? description, bool? isPublic, IFormFile? coverImage,
    bool removeCover = false) : IRequest<OperationResult>
{
    public Guid ReadingListId { get; set; } = readingListId;
    public string? Name { get; set; } = name;
    public string? Description { get; set; } = description;
    public bool? IsPublic { get; set; } = isPublic;
    public IFormFile? CoverImage { get; set; } = coverImage;
    public bool RemoveCover { get; set; } = removeCover;
}
