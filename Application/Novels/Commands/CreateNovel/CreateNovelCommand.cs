using Application.Users.Commands.FollowUser;
using MediatR;
using Microsoft.AspNetCore.Http;

namespace Application.Novels.Commands.CreateNovel;

public class CreateNovelCommand : IRequest<OperationResult>
{
    public string Title { get; set; } = default!;
    public string Summary { get; set; } = default!;
    public IFormFile CoverImageUrl { get; set; } = default!;
    public List<int> GenreIds { get; set; } = new List<int>();

}
