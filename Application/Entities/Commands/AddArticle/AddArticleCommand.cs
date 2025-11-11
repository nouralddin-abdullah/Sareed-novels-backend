using Application.Users.Commands.FollowUser;
using MediatR;

namespace Application.Entities.Commands.AddArticle;

public class AddArticleCommand : IRequest<OperationResult>
{
    public Guid EntityId { get; set; }
    public string Title { get; set; } = default!;
    public string Content { get; set; } = default!;
    public int OrderIndex { get; set; }
}
