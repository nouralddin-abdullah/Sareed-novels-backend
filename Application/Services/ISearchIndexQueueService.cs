namespace Application.Services;

public interface ISearchIndexQueueService
{
    // Novel methods
    Task QueueIndexAsync(Guid novelId);
    Task QueueUpdateAsync(Guid novelId);
    Task QueueDeleteAsync(Guid novelId);
    
    // User methods
    Task QueueUserIndexAsync(string userId);
    Task QueueUserUpdateAsync(string userId);
    Task QueueUserDeleteAsync(string userId);
    
    // Entity methods
    Task QueueEntityIndexAsync(Guid entityId);
    Task QueueEntityUpdateAsync(Guid entityId);
    Task QueueEntityDeleteAsync(Guid entityId);
}
