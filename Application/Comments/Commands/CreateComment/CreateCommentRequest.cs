using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.FileProviders;

namespace Application.Comments.Commands.CreateComment;

public class CreateCommentRequest
{
    public string Content { get; set; } = default!;
    public IFormFile? AttachedImage { get; set; }
    public Guid? ParentCommentId { get; set; }

}
