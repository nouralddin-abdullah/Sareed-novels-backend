using Domain.Entities;
using Domain.Repositories;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Repositories;

public class ReviewsRepository(ApplicationDbContext dbContext) : IReviewsRepository
{
    public async Task<bool> CreateOne(Review review)
    {
        await dbContext.Reviews.AddAsync(review);
        var result = await dbContext.SaveChangesAsync();
        if (result > 0)
        {
            return true;
        }
        return false;
    }

    public async Task<bool> DeleteReview(Review review)
    {
        dbContext.Reviews.Remove(review!);
        var result = await dbContext.SaveChangesAsync();
        return result > 0;
    }

    public async Task<bool> DeleteReviewWithNovelUpdate(Review review, Novel novel)
    {
        using var transaction = await dbContext.Database.BeginTransactionAsync();
        try
        {
            dbContext.Novels.Update(novel);
            dbContext.Reviews.Remove(review);
            var result = await dbContext.SaveChangesAsync();

            if (result > 0)
            {
                await transaction.CommitAsync();
                return true;
            }

            await transaction.RollbackAsync();
            return false;
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }
    }

    public async Task<(IEnumerable<Review>, int)> GetNovelReviews(Guid novelId, int PageSize, int PageNumber, string sorting)
    {
        var novelReviews = dbContext.Reviews.Include(r => r.ReviewOwner).Where(n => n.NovelId == novelId).AsQueryable();
        var totalCount = await novelReviews.CountAsync();
        if (PageNumber > 0 && PageSize > 0)
        {
            novelReviews = sorting?.ToLower() switch
            {
                "newest" => novelReviews.OrderByDescending(r => r.CreatedAt),
                "oldest" => novelReviews.OrderBy(r => r.CreatedAt),
                "likes" => novelReviews.OrderByDescending(r => r.LikeCount),
                _ => novelReviews.OrderByDescending(r => r.LikeCount)
            };

            novelReviews = novelReviews.Skip(PageSize * (PageNumber - 1)).Take(PageSize);
        }
        var novelReviewsList = await novelReviews.ToListAsync();
        return (novelReviewsList, totalCount);
    }

    public async Task<Review?> GetReviewById(Guid reviewId)
    {
        return await dbContext.Reviews.FirstOrDefaultAsync(r => r.Id == reviewId);
    }

    public async Task<Review?> GetUserReviewForNovel(string userId, Guid novelId)
    {
        return await dbContext.Reviews.FirstOrDefaultAsync(r => r.ReviewerId == userId && r.NovelId == novelId);
    }
}
