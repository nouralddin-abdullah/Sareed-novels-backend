using Application.Services;
using Application.Users;
using Application.Users.Commands.FollowUser;
using Domain.Entities;
using Domain.Exceptions;
using Domain.Repositories;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Application.Entities.Commands.AddArticle;

public class AddArticleCommandHandler(
    ILogger<AddArticleCommandHandler> logger,
    INovelEntityRepository entityRepository,
    IUserContext userContext,
    ISearchIndexQueueService searchQueue) : IRequestHandler<AddArticleCommand, OperationResult>
{
    public async Task<OperationResult> Handle(AddArticleCommand request, CancellationToken cancellationToken)
    {
        var currentUser = userContext.GetCurrentUser() ?? throw new ForbidException("User not authenticated");

        var entity = await entityRepository.GetEntityByIdAsync(request.EntityId);
        if (entity == null)
        {
            return new OperationResult
            {
                Success = false,
                Message = "Entity not found"
            };
        }

        // Verify user owns the novel (using included Novel)
        if (entity.Novel == null || entity.Novel.AuthorId != currentUser.Id)
        {
            return new OperationResult
            {
                Success = false,
                Message = "You don't have permission to add articles to this entity"
            };
        }

        var article = new EntityArticle
        {
            Id = Guid.NewGuid(),
            EntityId = request.EntityId,
            Title = request.Title,
            Content = request.Content,
            OrderIndex = request.OrderIndex,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        await entityRepository.AddArticleAsync(article);

        // Queue entity update for Elasticsearch (to include new article)
        await searchQueue.QueueEntityUpdateAsync(entity.Id);

        logger.LogInformation("Article {ArticleId} added to entity {EntityId}", article.Id, entity.Id);

        return new OperationResult
        {
            Success = true,
            Message = "Article added successfully"
        };
    }
}
