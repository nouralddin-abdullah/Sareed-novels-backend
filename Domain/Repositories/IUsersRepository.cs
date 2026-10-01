using Domain.Entities;
using Domain.Profiles;
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
    Task<Dictionary<string, bool>> IsFollowingBulkAsync(string currentUserId, IEnumerable<string> userIds);
    /// <summary>Adds the follow unless it exists (safe under concurrency); true when this call added it.</summary>
    Task<bool> FollowUser(string userId, string userToFollow);
    /// <summary>Removes the follow if it exists (safe under concurrency); true when this call removed it.</summary>
    Task<bool> UnFollowUser(string userId, string userToUnFollow);
    Task<(IEnumerable<Follow>, int)> GetFollowersList(string userId, int PageSize, int PageNumber);
    Task<(IEnumerable<Follow>, int)> GetFollowingList(string userId, int PageSize, int PageNumber);
    
    // New methods for search
    Task<User?> GetUserById(string userId);
    Task<IEnumerable<User>> GetAllUsers();
    Task<int> GetFollowersCount(string userId);
    Task<int> GetFollowingCount(string userId);
    Task<int> GetNovelsCount(string userId);

    /// <summary>
    /// The member who most recently gave up <paramref name="userName"/> (see <see cref="UserNameChange"/>), for old
    /// profile links. Only for names no live user holds: callers look the live name up first.
    /// </summary>
    Task<User?> GetByPreviousUserNameAsync(string userName, CancellationToken cancellationToken = default);

    /// <summary>These users (untracked) by id, in one query; ids without a user are left out.</summary>
    Task<Dictionary<string, User>> GetByIdsAsync(IReadOnlyCollection<string> userIds, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gives an account without a password its first one (POST /api/User/set-password), in one statement: only while it
    /// still has no password and isn't deleted, and writing only the password hash, a new security stamp and a new
    /// concurrency stamp. False when that no longer holds (a password was set meanwhile) or there is no such account.
    /// </summary>
    Task<bool> SetFirstPasswordAsync(string userId, string passwordHash, CancellationToken cancellationToken = default);

    /// <summary>
    /// Who may browse the member's review and comment lists (#61); null when there is no such account or it is deleted.
    /// </summary>
    Task<ProfileListPrivacy?> GetListPrivacyAsync(string userId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Sets who may browse the member's review and comment lists (#61), leaving one that is null as it is, in one
    /// statement that writes only those two columns and a new concurrency stamp; with neither, nothing is written.
    /// Answers the settings as they are then, or null when there is no such account or it is deleted.
    /// </summary>
    Task<ProfileListPrivacy?> SetListPrivacyAsync(string userId, ListVisibility? reviews, ListVisibility? comments,
        CancellationToken cancellationToken = default);
}
