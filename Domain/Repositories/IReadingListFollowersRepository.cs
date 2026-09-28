using Domain.Entities;

namespace Domain.Repositories;

public interface IReadingListFollowersRepository
{
    Task<ReadingListFollower?> GetAsync(Guid readingListId, string userId);
    /// <summary>Adds the follow unless it exists (safe under concurrency); true when this call added it.</summary>
    Task<bool> FollowAsync(ReadingListFollower follower);
    /// <summary>Removes the follow if it exists (safe under concurrency); true when this call removed it.</summary>
    Task<bool> UnfollowAsync(Guid readingListId, string userId);
    Task<bool> IsFollowingAsync(Guid readingListId, string userId);
    Task<int> GetFollowersCountAsync(Guid readingListId);
    Task<(IEnumerable<User>, int)> GetFollowersAsync(Guid readingListId, int pageNumber, int pageSize);
}
