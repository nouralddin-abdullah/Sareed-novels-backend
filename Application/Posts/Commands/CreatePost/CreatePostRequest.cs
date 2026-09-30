using Microsoft.AspNetCore.Http;

namespace Application.Posts.Commands.CreatePost;

/// <summary>
/// The multipart form of POST /api/posts. Each field is optional, but a post needs at least one of them; the rules
/// (<see cref="PostRules"/>) are checked by <see cref="CreatePostCommandValidator"/> when the command is handled.
/// </summary>
public class CreatePostRequest
{
    /// <summary>
    /// The text, trimmed; left out, empty or only whitespace is no text. Nullable, so that ASP.NET doesn't refuse a post
    /// without text itself (as ValidationFailed) before the post rules answer.
    /// </summary>
    public string? Content { get; set; }

    public IFormFile? Image { get; set; }
    public Guid? NovelId { get; set; }
}
