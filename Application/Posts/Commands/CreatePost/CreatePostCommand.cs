using MediatR;
using Microsoft.AspNetCore.Http;

namespace Application.Posts.Commands.CreatePost;

public class CreatePostCommand(string content, IFormFile? image, Guid? novelId) : IRequest<CreatePostResult>
{
    public string Content { get; set; } = content;
    public IFormFile? Image { get; set; } = image;
    public Guid? NovelId { get; set; } = novelId;
}
