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
            return new OperationResult { Success = false, Code = "ArticleNotFound", Message = "المقال غير موجود" };
        }

        var entity = await entityRepository.GetEntityByIdAsync(article.EntityId);
        if (entity == null)
        {
            return new OperationResult { Success = false, Code = "EntityNotFound", Message = "هذا المدخل غير موجود" };
        }

        var novel = await novelsRepository.GetOne(entity.NovelId);
        if (novel == null || novel.AuthorId != currentUser.Id)
        {
            return new OperationResult { Success = false, Code = "NotOwner", Message = "هذا الإجراء متاح لكاتب الرواية فقط" };
        }

        await entityRepository.DeleteArticleAsync(request.ArticleId);

        logger.LogInformation("Article {ArticleId} deleted", request.ArticleId);

        return new OperationResult { Success = true, Message = "حُذف المقال" };
    }
}
