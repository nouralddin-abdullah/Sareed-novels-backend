using MediatR;
using Microsoft.AspNetCore.Http;

namespace Application.Posts.Commands.CreatePost;

/// <summary>A new post by the signed-in member: text, a picture and a novel, at least one of them (<see cref="PostRules"/>).</summary>
public class CreatePostCommand(string? content, IFormFile? image, Guid? novelId) : IRequest<CreatePostResult>
{
    /// <summary>As sent; <see cref="PostRules.Normalize"/> trims it, and empty or only whitespace is no text.</summary>
    public string? Content { get; set; } = content;
    public IFormFile? Image { get; set; } = image;
    public Guid? NovelId { get; set; } = novelId;
}
