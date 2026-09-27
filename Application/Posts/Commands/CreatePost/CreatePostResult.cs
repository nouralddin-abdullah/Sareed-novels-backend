using Application.Posts.DTOs;
using Application.Users.Commands.FollowUser;

namespace Application.Posts.Commands.CreatePost;

/// <summary>The usual success and message, and the post that was created.</summary>
public class CreatePostResult : OperationResult
{
    /// <summary>
    /// The new post exactly as GET /api/posts/{id} and a user's post list return it; null when nothing was created.
    /// </summary>
    public PostDTO? Post { get; set; }
}
