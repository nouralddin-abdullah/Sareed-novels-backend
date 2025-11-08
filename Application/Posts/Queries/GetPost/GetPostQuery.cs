using Application.Posts.DTOs;
using MediatR;

namespace Application.Posts.Queries.GetPost;

public class GetPostQuery(Guid postId) : IRequest<PostDTO>
{
    public Guid PostId { get; set; } = postId;
}
