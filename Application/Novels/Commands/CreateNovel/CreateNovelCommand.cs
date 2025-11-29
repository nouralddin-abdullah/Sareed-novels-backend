using Application.Users.Commands.FollowUser;
using MediatR;
using Microsoft.AspNetCore.Http;

namespace Application.Novels.Commands.CreateNovel;

public class CreateNovelCommand : IRequest<CreateNovelResult>
{
    public string Title { get; set; } = default!;
    public string Summary { get; set; } = default!;
    public IFormFile CoverImageUrl { get; set; } = default!;
    public List<int> GenreIds { get; set; } = new List<int>();
}

public class CreateNovelResult : OperationResult
{
    public Guid? NovelId { get; set; }
}
