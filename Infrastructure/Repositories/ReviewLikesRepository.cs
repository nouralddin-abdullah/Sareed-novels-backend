using Domain.Entities;
using Domain.Repositories;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Repositories;

public class ReviewLikesRepository(ApplicationDbContext dbContext) : IReviewLikesRepository
{
    public async Task<HashSet<Guid>> GetUserLikedReviewIds(string userId, IEnumerable<Guid> reviewIds)
    {
        var likedReviewIds = await dbContext.ReviewLikes
        .Where(rl => rl.UserId == userId && reviewIds.Contains(rl.ReviewId))
        .Select(rl => rl.ReviewId)
        .ToListAsync();

        return new HashSet<Guid>(likedReviewIds);
    }

    public async Task<ReviewLike?> GetUserLikeForReview(string userId, Guid reviewId)
    {
        return await dbContext.ReviewLikes
           .FirstOrDefaultAsync(rl => rl.UserId == userId && rl.ReviewId == reviewId);
    }

    public async Task<bool> HasUserLikedReview(string userId, Guid reviewId)
    {
        return await dbContext.ReviewLikes.AnyAsync(rl => rl.UserId == userId && rl.ReviewId == reviewId);

    }

    public async Task<bool> LikeReview(string userId, Guid reviewId)
    {
        // See PostLikesRepository.LikePost: insert once under concurrency, count in the same transaction.
        await using var transaction = await dbContext.Database.BeginTransactionAsync();
        var inserted = await dbContext.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO ReviewLikes (UserId, ReviewId, CreatedAt)
            SELECT {userId}, {reviewId}, {DateTime.UtcNow}
            WHERE NOT EXISTS (
                SELECT 1 FROM ReviewLikes WITH (UPDLOCK, HOLDLOCK) WHERE UserId = {userId} AND ReviewId = {reviewId})
            """);
        if (inserted == 1)
        {
            await dbContext.Reviews
                .Where(r => r.Id == reviewId)
                .ExecuteUpdateAsync(s => s.SetProperty(r => r.LikeCount, r => r.LikeCount + 1));
        }

        await transaction.CommitAsync();
        return inserted == 1;
    }

    public async Task<bool> UnLikeReview(string userId, Guid reviewId)
    {
        await using var transaction = await dbContext.Database.BeginTransactionAsync();
        var deleted = await dbContext.ReviewLikes
            .Where(rl => rl.UserId == userId && rl.ReviewId == reviewId)
            .ExecuteDeleteAsync();
        if (deleted > 0)
        {
            await dbContext.Reviews
                .Where(r => r.Id == reviewId)
                .ExecuteUpdateAsync(s => s.SetProperty(r => r.LikeCount, r => r.LikeCount > 0 ? r.LikeCount - 1 : 0));
        }

        await transaction.CommitAsync();
        return deleted > 0;
    }
}
