using Domain.Entities;
using Domain.Repositories;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Repositories;

public class ReviewsRepository(ApplicationDbContext dbContext) : IReviewsRepository
{
    public async Task<bool> CreateOne(Review review)
    {
        await using (var transaction = await dbContext.Database.BeginTransactionAsync())
        {
            await dbContext.Reviews.AddAsync(review);
            if (await dbContext.SaveChangesAsync() == 0)
            {
                return false;
            }

            await dbContext.Users
                .Where(u => u.Id == review.ReviewerId)
                .ExecuteUpdateAsync(s => s.SetProperty(u => u.ReviewsCount, u => u.ReviewsCount + 1));
            await transaction.CommitAsync();
        }

        // After the commit, not inside it: the stats are a recount, so the last writer sees every committed review
        // and a failure here heals on the novel's next review.
        await RefreshNovelReviewStats(review.NovelId);
        return true;
    }

    public async Task<bool> DeleteReview(Review review)
    {
        await using (var transaction = await dbContext.Database.BeginTransactionAsync())
        {
            // Review likes reference the review without a cascade.
            await dbContext.ReviewLikes.Where(rl => rl.ReviewId == review.Id).ExecuteDeleteAsync();
            if (await dbContext.Reviews.Where(r => r.Id == review.Id).ExecuteDeleteAsync() == 0)
            {
                return false;
            }

            await dbContext.Users
                .Where(u => u.Id == review.ReviewerId)
                .ExecuteUpdateAsync(s => s.SetProperty(u => u.ReviewsCount, u => u.ReviewsCount > 0 ? u.ReviewsCount - 1 : 0));
            await transaction.CommitAsync();
        }

        await RefreshNovelReviewStats(review.NovelId);
        return true;
    }

    public Task RefreshNovelReviewStats(Guid novelId)
    {
        var reviews = dbContext.Reviews;
        return dbContext.Novels
            .IgnoreQueryFilters()
            .Where(n => n.Id == novelId)
            .ExecuteUpdateAsync(s => s
                .SetProperty(n => n.ReviewCount, n => reviews.Count(r => r.NovelId == n.Id))
                .SetProperty(n => n.AverageWritingQualityScore,
                    n => reviews.Where(r => r.NovelId == n.Id).Average(r => (decimal?)r.WritingQualityScore) ?? 0)
                .SetProperty(n => n.AverageUpdatingStabilityScore,
                    n => reviews.Where(r => r.NovelId == n.Id).Average(r => (decimal?)r.UpdatingStabilityScore) ?? 0)
                .SetProperty(n => n.AverageCharacterDevelopmentScore,
                    n => reviews.Where(r => r.NovelId == n.Id).Average(r => (decimal?)r.CharacterDevelopmentScore) ?? 0)
                .SetProperty(n => n.AverageWorldBuildingScore,
                    n => reviews.Where(r => r.NovelId == n.Id).Average(r => (decimal?)r.WorldBuildingScore) ?? 0)
                .SetProperty(n => n.TotalAverageScore,
                    n => ((reviews.Where(r => r.NovelId == n.Id).Average(r => (decimal?)r.WritingQualityScore) ?? 0)
                          + (reviews.Where(r => r.NovelId == n.Id).Average(r => (decimal?)r.UpdatingStabilityScore) ?? 0)
                          + (reviews.Where(r => r.NovelId == n.Id).Average(r => (decimal?)r.CharacterDevelopmentScore) ?? 0)
                          + (reviews.Where(r => r.NovelId == n.Id).Average(r => (decimal?)r.WorldBuildingScore) ?? 0)) / 4));
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
