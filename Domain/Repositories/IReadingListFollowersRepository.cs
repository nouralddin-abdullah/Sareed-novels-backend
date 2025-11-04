using Domain.Entities;

namespace Domain.Repositories;

public interface IReadingListFollowersRepository
{
    Task<ReadingListFollower?> GetAsync(Guid readingListId, string userId);
    Task<bool> FollowAsync(ReadingListFollower follower);
    Task<bool> UnfollowAsync(Guid readingListId, string userId);
    Task<bool> IsFollowingAsync(Guid readingListId, string userId);
    Task<int> GetFollowersCountAsync(Guid readingListId);
    Task<(IEnumerable<User>, int)> GetFollowersAsync(Guid readingListId, int pageNumber, int pageSize);
}
