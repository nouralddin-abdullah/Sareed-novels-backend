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

    public async Task<bool> LikeReview(ReviewLike reviewLike)
    {
        try
        {
            await dbContext.ReviewLikes.AddAsync(reviewLike);
            await dbContext.SaveChangesAsync();

            //increment like on review
            var review = await dbContext.Reviews.FindAsync(reviewLike.ReviewId);
            if (review != null)
            {
                review.IncrementLikeCount();
                dbContext.Reviews.Update(review);
                await dbContext.SaveChangesAsync();
            }
            return true;
        }
        catch (DbUpdateException)
        {
            return false;
        }
    }

    public async Task<bool> UnLikeReview(string userId, Guid reviewId)
    {
        try
        {
            var existingLike = await dbContext.ReviewLikes.FirstOrDefaultAsync(rl => rl.UserId == userId && rl.ReviewId == reviewId);
            if (existingLike == null)
                return false;
            dbContext.ReviewLikes.Remove(existingLike);
            await dbContext.SaveChangesAsync();

            //decrease like count
            var review = await dbContext.Reviews.FindAsync(reviewId);
            if (review != null)
            {
                review.DecrementLikeCount();
                dbContext.Reviews.Update(review);
                await dbContext.SaveChangesAsync();
            }
            return true;
        }
        catch
        {
            return false;
        }
    }
}
