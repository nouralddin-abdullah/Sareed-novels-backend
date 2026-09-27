using Application.Comments.DTOS;
using Application.Users.Commands.FollowUser;

namespace Application.Comments.Commands.CreateComment;

/// <summary>The usual success and message, and the comment that was created.</summary>
public class CreateCommentResult : OperationResult
{
    /// <summary>
    /// The new comment exactly as the chapter, paragraph and post comment lists return it (a reply carries its
    /// ParentCommentId); null when nothing was created.
    /// </summary>
    public CommentsDTO? Comment { get; set; }
}
