using Domain.Entities;
using Domain.Repositories;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Repositories;

public class CommentLikesRepository(ApplicationDbContext dbContext) : ICommentLikesRepository
{
    public async Task<HashSet<Guid>> GetUserLikedCommentIds(string userId, IEnumerable<Guid> commentIds)
    {
        var likedCommentIds = await dbContext.CommentLikes
            .Where(cl => cl.UserId == userId && commentIds.Contains(cl.CommentId))
            .Select(cl => cl.CommentId)
            .ToListAsync();

        return new HashSet<Guid>(likedCommentIds);
    }

    public async Task<CommentLikes?> GetUserLikeForComment(string userId, Guid commentId)
    {
        return await dbContext.CommentLikes
            .FirstOrDefaultAsync(cl => cl.UserId == userId && cl.CommentId == commentId);
    }

    public async Task<bool> HasUserLikedComment(string userId, Guid commentId)
    {
        return await dbContext.CommentLikes
            .AnyAsync(cl => cl.UserId == userId && cl.CommentId == commentId);
    }

    public async Task<bool> LikeComment(string userId, Guid commentId)
    {
        // See PostLikesRepository.LikePost: insert once under concurrency, count in the same transaction.
        await using var transaction = await dbContext.Database.BeginTransactionAsync();
        var inserted = await dbContext.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO CommentLikes (UserId, CommentId, CreatedAt)
            SELECT {userId}, {commentId}, {DateTime.UtcNow}
            WHERE NOT EXISTS (
                SELECT 1 FROM CommentLikes WITH (UPDLOCK, HOLDLOCK) WHERE UserId = {userId} AND CommentId = {commentId})
            """);
        if (inserted == 1)
        {
            await dbContext.Comments
                .IgnoreQueryFilters()
                .Where(c => c.Id == commentId)
                .ExecuteUpdateAsync(s => s.SetProperty(c => c.LikesCount, c => c.LikesCount + 1));
        }

        await transaction.CommitAsync();
        return inserted == 1;
    }

    public async Task<bool> UnLikeComment(string userId, Guid commentId)
    {
        await using var transaction = await dbContext.Database.BeginTransactionAsync();
        var deleted = await dbContext.CommentLikes
            .Where(cl => cl.UserId == userId && cl.CommentId == commentId)
            .ExecuteDeleteAsync();
        if (deleted > 0)
        {
            await dbContext.Comments
                .IgnoreQueryFilters()
                .Where(c => c.Id == commentId)
                .ExecuteUpdateAsync(s => s.SetProperty(c => c.LikesCount, c => c.LikesCount > 0 ? c.LikesCount - 1 : 0));
        }

        await transaction.CommitAsync();
        return deleted > 0;
    }
}
