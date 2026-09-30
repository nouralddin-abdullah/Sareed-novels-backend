using System.Security.Cryptography;
using Domain.Entities;
using Domain.Repositories;
using Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Repositories
{
    public class UsersRepositories(UserManager<User> userManager, ApplicationDbContext dbContext) : IUsersRepository
    {

        public async Task<IdentityResult> Create(User user, string password)
        {
            return await userManager.CreateAsync(user, password);
        }

        public async Task<IdentityResult> ConfirmEmail(User user, string token)
        {
            return await userManager.ConfirmEmailAsync(user, token);
        }

        public async Task<string> GenerateEmailToken(User user)
        {
            return await userManager.GenerateEmailConfirmationTokenAsync(user);
        }

        public async Task<IEnumerable<Follow>> GetRecentFollowers(User user, int count = 7)
        {
            return await dbContext.Follows
                .Where(f => f.FollowedId == user.Id)
                .OrderByDescending(f => f.CreatedAt)
                .Take(count)
                .Include(f => f.Follower)
                .ToListAsync();
        }

        public async Task<IEnumerable<Follow>> GetRecentFollowing(User user, int count = 7)
        {
            return await dbContext.Follows
                .Where(f => f.FollowerId == user.Id)
                .OrderByDescending(f => f.CreatedAt)
                .Take(count)
                .Include(f => f.Followed)
                .ToListAsync();
        }

        // Deleted accounts are left out of follower and following lists and totals. Deleting an account removes its
        // follows; these filters also cover a follow that raced the deletion.
        public async Task<int> GetFollowersCount(User user)
        {
            return await dbContext.Follows
                .CountAsync(f => f.FollowedId == user.Id && f.Follower.DeletedAt == null);
        }

        public async Task<int> GetFollowingCount(User user)
        {
            return await dbContext.Follows
                .CountAsync(f => f.FollowerId == user.Id && f.Followed.DeletedAt == null);
        }

        public async Task<bool> IsFollowingAsync(string userId, string otherUserId)
        {
            return await dbContext.Follows.AnyAsync(f => f.FollowerId == userId && f.FollowedId == otherUserId);
        }

        public async Task<Dictionary<string, bool>> IsFollowingBulkAsync(string currentUserId, IEnumerable<string> userIds)
        {
            var userIdsList = userIds.ToList();
            
            // Single query to get all follows
            var followedUserIds = await dbContext.Follows
                .Where(f => f.FollowerId == currentUserId && userIdsList.Contains(f.FollowedId))
                .Select(f => f.FollowedId)
                .ToListAsync();
            
            // Create dictionary with all users, default false
            var result = userIdsList.ToDictionary(id => id, id => false);
            
            // Mark followed users as true
            foreach (var followedId in followedUserIds)
            {
                result[followedId] = true;
            }
            
            return result;
        }

        public async Task<bool> FollowUser(string userId, string userToFollow)
        {
            // Two follows at once (a double tap, a retry) insert one row: the key lock makes the second a no-op instead
            // of a primary key violation, which answered 500 (#25).
            var inserted = await dbContext.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO Follows (FollowerId, FollowedId, CreatedAt)
                SELECT {userId}, {userToFollow}, {DateTime.UtcNow}
                WHERE NOT EXISTS (SELECT 1 FROM Follows WITH (UPDLOCK, HOLDLOCK)
                                  WHERE FollowerId = {userId} AND FollowedId = {userToFollow})
                """);
            return inserted == 1;
        }

        public async Task<bool> UnFollowUser(string userId, string userToUnFollow) =>
            // One statement: a concurrent unfollow deletes nothing (it used to fail on the row the other one removed).
            await dbContext.Follows
                .Where(f => f.FollowerId == userId && f.FollowedId == userToUnFollow)
                .ExecuteDeleteAsync() > 0;

        public async Task<(IEnumerable<Follow>, int)> GetFollowersList(string userId, int PageSize, int PageNumber)
        {
            var followers = dbContext.Follows.Where(f => f.FollowedId == userId && f.Follower.DeletedAt == null).Include(f => f.Follower).AsQueryable();
            var totalCount = await followers.CountAsync();
            if (PageNumber > 0 && PageSize > 0)
            {
                followers = followers.OrderBy(f => f.CreatedAt).Skip(PageSize * (PageNumber - 1)).Take(PageSize);
            }
            var followersList = await followers.ToListAsync();
            return (followersList, totalCount);
        }

        public async Task<(IEnumerable<Follow>, int)> GetFollowingList(string userId, int PageSize, int PageNumber)
        {
            var following = dbContext.Follows.Where(f => f.FollowerId == userId && f.Followed.DeletedAt == null).Include(f => f.Followed).AsQueryable();
            var totalCount = await following.CountAsync();
            if (PageNumber > 0 && PageSize > 0)
            {
                following = following.OrderBy(f => f.CreatedAt).Skip(PageSize * (PageNumber - 1)).Take(PageSize);
            }
            var followingList = await following.ToListAsync();
            return (followingList, totalCount);
        }

        // New methods for search
        public async Task<User?> GetUserById(string userId)
        {
            return await userManager.FindByIdAsync(userId);
        }

        public async Task<IEnumerable<User>> GetAllUsers()
        {
            return await dbContext.Users.ToListAsync();
        }

        public async Task<int> GetFollowersCount(string userId)
        {
            return await dbContext.Follows
                .CountAsync(f => f.FollowedId == userId && f.Follower.DeletedAt == null);
        }

        public async Task<int> GetFollowingCount(string userId)
        {
            return await dbContext.Follows
                .CountAsync(f => f.FollowerId == userId && f.Followed.DeletedAt == null);
        }

        public async Task<int> GetNovelsCount(string userId)
        {
            return await dbContext.Novels
                .Where(n => n.AuthorId == userId && !n.IsDraft && !n.IsDeleted)
                .CountAsync();
        }

        public async Task<User?> GetByPreviousUserNameAsync(string userName, CancellationToken cancellationToken = default)
        {
            var normalized = userManager.NormalizeName(userName);
            // The latest to give the name up, if a name went through several members.
            var userId = await dbContext.UserNameChanges
                .Where(c => c.OldNormalizedUserName == normalized)
                .OrderByDescending(c => c.ChangedAt)
                .ThenByDescending(c => c.Id)
                .Select(c => c.UserId)
                .FirstOrDefaultAsync(cancellationToken);

            // A deleted account's old names were removed with it; a deleted account is never found this way.
            var user = userId == null ? null : await userManager.FindByIdAsync(userId);
            return user?.DeletedAt == null ? user : null;
        }

        public async Task<Dictionary<string, User>> GetByIdsAsync(IReadOnlyCollection<string> userIds, CancellationToken cancellationToken = default)
        {
            var ids = userIds.Distinct().ToList();
            if (ids.Count == 0)
            {
                return [];
            }

            return await dbContext.Users
                .AsNoTracking()
                .Where(u => ids.Contains(u.Id))
                .ToDictionaryAsync(u => u.Id, StringComparer.OrdinalIgnoreCase, cancellationToken);
        }

        public async Task<bool> SetFirstPasswordAsync(
            string userId, string passwordHash, CancellationToken cancellationToken = default)
        {
            // UserManager.AddPasswordAsync would save the whole row from the copy read at the start of the request,
            // writing back counters, a suspension or a sign-out-everywhere that changed meanwhile. This writes three
            // columns:
            // - the hash, only if there is still none: of two requests at once, one sets it and the other gets false;
            // - a new security stamp, as UserManager gives every password change: reset and confirmation links sent
            //   before stop working. Access tokens don't carry the stamp, so every session stays signed in;
            // - a new concurrency stamp, so an update through UserManager that read the account before this
            //   (update-me) fails on it instead of writing back the row without a password.
            var securityStamp = Convert.ToHexString(RandomNumberGenerator.GetBytes(20));
            var concurrencyStamp = Guid.NewGuid().ToString();
            var updated = await dbContext.Users
                .Where(u => u.Id == userId && u.PasswordHash == null && u.DeletedAt == null)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(u => u.PasswordHash, passwordHash)
                    .SetProperty(u => u.SecurityStamp, securityStamp)
                    .SetProperty(u => u.ConcurrencyStamp, concurrencyStamp), cancellationToken);
            return updated == 1;
        }
    }
}
