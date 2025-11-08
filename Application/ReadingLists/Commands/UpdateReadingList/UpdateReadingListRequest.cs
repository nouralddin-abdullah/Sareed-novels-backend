using Microsoft.AspNetCore.Http;

namespace Application.ReadingLists.Commands.UpdateReadingList;

public class UpdateReadingListRequest
{
    public string? Name { get; set; }
    public string? Description { get; set; }
    public bool? IsPublic { get; set; }
    public IFormFile? CoverImage { get; set; }
}
