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

        public async Task<int> GetFollowersCount(User user)
        {
            return await dbContext.Follows
                .CountAsync(f => f.FollowedId == user.Id);
        }

        public async Task<int> GetFollowingCount(User user)
        {
            return await dbContext.Follows
                .CountAsync(f => f.FollowerId == user.Id);
        }

        public async Task<bool> IsFollowingAsync(string userId, string otherUserId)
        {
            return await dbContext.Follows.AnyAsync(f => f.FollowerId == userId && f.FollowedId == otherUserId);
        }

        public async Task<bool> FollowUser(string userId, string userToFollow)
        {
            var follow = new Follow
            {
                FollowerId = userId,
                FollowedId = userToFollow,
                CreatedAt = DateTime.UtcNow
            };
            await dbContext.Follows.AddAsync(follow);
            var result = await dbContext.SaveChangesAsync();
            return result > 0;
        }

        public async Task<bool> UnFollowUser(string userId, string userToUnFollow)
        {
            var follow = await dbContext.Follows.FirstOrDefaultAsync(f => f.FollowerId == userId && f.FollowedId == userToUnFollow);
            dbContext.Follows.Remove(follow!);
            var result = await dbContext.SaveChangesAsync();
            return result > 0;
        }

        public async Task<(IEnumerable<Follow>, int)> GetFollowersList(string userId, int PageSize, int PageNumber)
        {
            var followers = dbContext.Follows.Where(f => f.FollowedId == userId).Include(f => f.Follower).AsQueryable();
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
            var following = dbContext.Follows.Where(f => f.FollowerId == userId).Include(f => f.Followed).AsQueryable();
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
                .CountAsync(f => f.FollowedId == userId);
        }

        public async Task<int> GetFollowingCount(string userId)
        {
            return await dbContext.Follows
                .CountAsync(f => f.FollowerId == userId);
        }

        public async Task<int> GetNovelsCount(string userId)
        {
            return await dbContext.Novels
                .Where(n => n.AuthorId == userId && !n.IsDraft && !n.IsDeleted)
                .CountAsync();
        }
    }
}
