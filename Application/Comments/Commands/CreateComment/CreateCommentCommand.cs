using Application.Users.Commands.FollowUser;
using MediatR;
using Microsoft.AspNetCore.Http;

namespace Application.Comments.Commands.CreateComment;

public class CreateCommentCommand(Guid? chapterId, Guid? paragraphId, Guid? postId, string content, IFormFile? attachedImage, Guid? parentCommentId) : IRequest<OperationResult>
{
    public Guid? ChapterId { get; set; } = chapterId;
    public Guid? ParagraphId { get; set; } = paragraphId;
    public Guid? PostId { get; set; } = postId;
    public string Content { get; set; } = content;
    public IFormFile? AttachedImage { get; set; } = attachedImage;
    public Guid? ParentCommentId { get; set; } = parentCommentId;
}
