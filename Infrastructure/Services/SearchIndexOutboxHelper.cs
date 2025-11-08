using Application.Services;
using Domain.Entities;
using Domain.Repositories;
using Microsoft.Extensions.Logging;

namespace Infrastructure.Services;

public class SearchIndexQueueService(
    ISearchIndexOutboxRepository outboxRepository,
    ILogger<SearchIndexQueueService> logger) : ISearchIndexQueueService
{
    public async Task QueueIndexAsync(Guid novelId)
    {
        await QueueActionAsync("Novel", novelId.ToString(), "Index");
    }

    public async Task QueueUpdateAsync(Guid novelId)
    {
        await QueueActionAsync("Novel", novelId.ToString(), "Update");
    }

    public async Task QueueDeleteAsync(Guid novelId)
    {
        await QueueActionAsync("Novel", novelId.ToString(), "Delete");
    }

    // User methods
    public async Task QueueUserIndexAsync(string userId)
    {
        await QueueActionAsync("User", userId, "Index");
    }

    public async Task QueueUserUpdateAsync(string userId)
    {
        await QueueActionAsync("User", userId, "Update");
    }

    public async Task QueueUserDeleteAsync(string userId)
    {
        await QueueActionAsync("User", userId, "Delete");
    }

    private async Task QueueActionAsync(string entityType, string entityId, string action)
    {
        try
        {
            var entry = new SearchIndexOutbox
            {
                Id = Guid.NewGuid(),
                EntityType = entityType,
                EntityId = Guid.TryParse(entityId, out var guidId) ? guidId : Guid.Empty,
                Action = action,
                CreatedAt = DateTime.UtcNow,
                Processed = false,
                RetryCount = 0
            };

            // Store userId in ErrorMessage field temporarily (we'll fix schema later if needed)
            if (entityType == "User")
            {
                entry.ErrorMessage = entityId; // Store userId here
            }

            await outboxRepository.AddOutboxEntryAsync(entry);

            logger.LogDebug(
                "Queued search index action: {Action} for {EntityType} {EntityId}",
                action,
                entityType,
                entityId
            );
        }
        catch (Exception ex)
        {
            logger.LogError(
                ex,
                "Failed to queue search index action: {Action} for {EntityType} {EntityId}",
                action,
                entityType,
                entityId
            );
        }
    }
}
