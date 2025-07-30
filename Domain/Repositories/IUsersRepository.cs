using Domain.Entities;
using Microsoft.AspNetCore.Identity;
namespace Domain.Repositories;

public interface IUsersRepository
{
    public Task<IdentityResult> Create(User user, string password);
    public Task<IdentityResult> ConfirmEmail(User user, string token);
    public Task<string> GenerateEmailToken(User user);
    Task<IEnumerable<Follow>> GetRecentFollowers(User user, int count = 7);
    Task<IEnumerable<Follow>> GetRecentFollowing(User user, int count = 7);
    Task<int> GetFollowersCount(User user);
    Task<int> GetFollowingCount(User user);
    Task<bool> IsFollowingAsync(string userId, string otherUserId);
    Task<bool> FollowUser(string userId, string userToFollow);
    Task<bool> UnFollowUser(string userId, string userToUnFollow);
    Task<(IEnumerable<Follow>, int)> GetFollowersList(string userId, int PageSize, int PageNumber);
    Task<(IEnumerable<Follow>, int)> GetFollowingList(string userId, int PageSize, int PageNumber);


}
