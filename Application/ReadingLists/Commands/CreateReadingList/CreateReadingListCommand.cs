using MediatR;
using Microsoft.AspNetCore.Http;

namespace Application.ReadingLists.Commands.CreateReadingList;

public class CreateReadingListCommand : IRequest<CreateReadingListResult>
{
    public string Name { get; set; } = default!;
    public string? Description { get; set; }
    public IFormFile? CoverImage { get; set; }
    public bool IsPublic { get; set; } = false;

    /// <summary>
    /// A novel to put on the new list in the same step (the "new list" button on a novel page), checked as adding a novel
    /// to a list is; if it can't be added, no list is created.
    /// </summary>
    public Guid? NovelId { get; set; }
}
