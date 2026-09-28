using Application.Services;
using Application.Users;
using Application.Users.Commands.FollowUser;
using Domain.Exceptions;
using Domain.Repositories;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Application.Entities.Commands.UpdateArticle;

public class UpdateArticleCommandHandler(
    ILogger<UpdateArticleCommandHandler> logger,
    INovelEntityRepository entityRepository,
    IUserContext userContext) : IRequestHandler<UpdateArticleCommand, OperationResult>
{
    public async Task<OperationResult> Handle(UpdateArticleCommand request, CancellationToken cancellationToken)
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

        // Verify user owns the novel (using included Novel)
        if (entity.Novel == null || entity.Novel.AuthorId != currentUser.Id)
        {
            return new OperationResult { Success = false, Code = "NotOwner", Message = "هذا الإجراء متاح لكاتب الرواية فقط" };
        }

        // Update only provided fields
        if (request.Title != null) article.Title = request.Title;
        if (request.Content != null) article.Content = request.Content;
        if (request.OrderIndex.HasValue) article.OrderIndex = request.OrderIndex.Value;

        article.UpdatedAt = DateTime.UtcNow;

        await entityRepository.UpdateArticleAsync(article);

        logger.LogInformation("Article {ArticleId} updated", article.Id);

        return new OperationResult { Success = true, Message = "حُفظ المقال" };
    }
}
