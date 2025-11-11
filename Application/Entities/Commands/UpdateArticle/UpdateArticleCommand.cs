using Application.Users.Commands.FollowUser;
using MediatR;

namespace Application.Entities.Commands.UpdateArticle;

public class UpdateArticleCommand : IRequest<OperationResult>
{
    public Guid ArticleId { get; set; }
    public string? Title { get; set; }
    public string? Content { get; set; }
    public int? OrderIndex { get; set; }
}
