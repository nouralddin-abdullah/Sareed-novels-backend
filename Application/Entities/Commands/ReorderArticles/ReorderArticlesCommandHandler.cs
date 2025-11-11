using Application.Users;
using Domain.Exceptions;
using Domain.Repositories;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Application.Entities.Commands.ReorderArticles;

public class ReorderArticlesCommandHandler(
    ILogger<ReorderArticlesCommandHandler> logger,
    INovelEntityRepository entityRepository,
    INovelsRepository novelsRepository,
    IUserContext userContext) : IRequestHandler<ReorderArticlesCommand, bool>
{
    public async Task<bool> Handle(ReorderArticlesCommand request, CancellationToken cancellationToken)
    {
        logger.LogInformation("Reordering articles for entity {EntityId}", request.EntityId);
        
        var currentUser = userContext.GetCurrentUser() ?? throw new ForbidException("User not authenticated");
        
        // Get entity with articles
        var entity = await entityRepository.GetEntityByIdAsync(request.EntityId);
        if (entity == null)
        {
            logger.LogWarning("Entity {EntityId} not found", request.EntityId);
            return false;
        }
        
        // Verify user owns the novel
        var novel = await novelsRepository.GetOne(entity.NovelId);
        if (novel == null || novel.AuthorId != currentUser.Id)
        {
            throw new ForbidException("User doesn't own this novel");
        }
        
        // Reorder articles
        var result = await entityRepository.ReorderArticlesAsync(request.EntityId, request.OrderedArticleIds);
        
        if (result)
        {
            logger.LogInformation("Articles reordered successfully for entity {EntityId}", request.EntityId);
        }
        else
        {
            logger.LogWarning("Failed to reorder articles for entity {EntityId}", request.EntityId);
        }
        
        return result;
    }
}
