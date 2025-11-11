using Application.Users.Commands.FollowUser;
using MediatR;

namespace Application.Entities.Commands.DeleteArticle;

public class DeleteArticleCommand(Guid articleId) : IRequest<OperationResult>
{
    public Guid ArticleId { get; set; } = articleId;
}
