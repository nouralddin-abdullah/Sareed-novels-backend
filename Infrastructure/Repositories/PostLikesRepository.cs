using Domain.Entities;
using Domain.Repositories;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Repositories;

public class PostLikesRepository(ApplicationDbContext dbContext) : IPostLikesRepository
{
    public async Task<PostLike?> GetUserLikeForPost(string userId, Guid postId)
    {
        return await dbContext.PostLikes
            .AsNoTracking()
            .FirstOrDefaultAsync(pl => pl.UserId == userId && pl.PostId == postId);
    }

    public async Task<HashSet<Guid>> GetUserLikedPostIds(string userId, IEnumerable<Guid> postIds)
    {
        var postIdsList = postIds as List<Guid> ?? postIds.ToList();
        
        var likedPostIds = await dbContext.PostLikes
            .AsNoTracking()
            .Where(pl => pl.UserId == userId && postIdsList.Contains(pl.PostId))
            .Select(pl => pl.PostId)
            .ToListAsync();
        
        return new HashSet<Guid>(likedPostIds);
    }

    public async Task<bool> HasUserLikedPost(string userId, Guid postId)
    {
        return await dbContext.PostLikes
            .AsNoTracking()
            .AnyAsync(pl => pl.UserId == userId && pl.PostId == postId);
    }

    public async Task<bool> LikePost(string userId, Guid postId)
    {
        // The UPDLOCK/HOLDLOCK existence check makes a concurrent double like insert once (and not hit the unique
        // index); the count moves in the same transaction as the row.
        await using var transaction = await dbContext.Database.BeginTransactionAsync();
        var inserted = await dbContext.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO PostLikes (UserId, PostId, CreatedAt)
            SELECT {userId}, {postId}, {DateTime.UtcNow}
            WHERE NOT EXISTS (
                SELECT 1 FROM PostLikes WITH (UPDLOCK, HOLDLOCK) WHERE UserId = {userId} AND PostId = {postId})
            """);
        if (inserted == 1)
        {
            await dbContext.Posts
                .IgnoreQueryFilters()
                .Where(p => p.Id == postId)
                .ExecuteUpdateAsync(s => s.SetProperty(p => p.LikesCount, p => p.LikesCount + 1));
        }

        await transaction.CommitAsync();
        return inserted == 1;
    }

    public async Task<bool> UnLikePost(string userId, Guid postId)
    {
        await using var transaction = await dbContext.Database.BeginTransactionAsync();
        var deleted = await dbContext.PostLikes
            .Where(pl => pl.UserId == userId && pl.PostId == postId)
            .ExecuteDeleteAsync();
        if (deleted > 0)
        {
            await dbContext.Posts
                .IgnoreQueryFilters()
                .Where(p => p.Id == postId)
                .ExecuteUpdateAsync(s => s.SetProperty(p => p.LikesCount, p => p.LikesCount > 0 ? p.LikesCount - 1 : 0));
        }

        await transaction.CommitAsync();
        return deleted > 0;
    }
}
