using Microsoft.AspNetCore.Http;

namespace Application.Novels.Commands.ChangeCover
{
    public class ChangeCoverCommandRequest
    {
        public required IFormFile CoverUrl { get; init; }
    }
}
