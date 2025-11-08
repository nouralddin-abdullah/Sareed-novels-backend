using Microsoft.AspNetCore.Http;

namespace Application.Posts.Commands.CreatePost;

public class CreatePostRequest
{
    public string Content { get; set; } = default!;
    public IFormFile? Image { get; set; }
    public Guid? NovelId { get; set; }
}
