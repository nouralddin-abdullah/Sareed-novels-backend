using Application.Services;
using Application.Users;
using Application.Users.Commands.FollowUser;
using Domain.Exceptions;
using Domain.Repositories;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Application.Entities.Commands.DeleteArticle;

public class DeleteArticleCommandHandler(
    ILogger<DeleteArticleCommandHandler> logger,
    INovelEntityRepository entityRepository,
    INovelsRepository novelsRepository,
    IUserContext userContext) : IRequestHandler<DeleteArticleCommand, OperationResult>
{
    public async Task<OperationResult> Handle(DeleteArticleCommand request, CancellationToken cancellationToken)
    {
        var currentUser = userContext.GetCurrentUser() ?? throw new ForbidException("سجّل الدخول للمتابعة", "NotSignedIn");

        var article = await entityRepository.GetArticleByIdAsync(request.ArticleId);
        if (article == null)
        {
            return new OperationResult { Success = false, Code = "ArticleNotFound", Message = "Article not found" };
        }

        var entity = await entityRepository.GetEntityByIdAsync(article.EntityId);
        if (entity == null)
        {
            return new OperationResult { Success = false, Code = "EntityNotFound", Message = "Entity not found" };
        }

        var novel = await novelsRepository.GetOne(entity.NovelId);
        if (novel == null || novel.AuthorId != currentUser.Id)
        {
            return new OperationResult { Success = false, Code = "NotOwner", Message = "Permission denied" };
        }

        await entityRepository.DeleteArticleAsync(request.ArticleId);

        logger.LogInformation("Article {ArticleId} deleted", request.ArticleId);

        return new OperationResult { Success = true, Message = "Article deleted successfully" };
    }
}
