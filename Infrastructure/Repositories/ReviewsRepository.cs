using Domain.Entities;
using Domain.Repositories;
using Domain.Reviews;
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

    public async Task<bool> UpdateReview(Review review, ReviewEdit edit, DateTime updatedAt)
    {
        var writing = edit.WritingQualityScore;
        var stability = edit.UpdatingStabilityScore;
        var characters = edit.CharacterDevelopmentScore;
        var world = edit.WorldBuildingScore;
        var replacesContent = edit.ReplacesContent;
        var content = edit.Content;
        var isSpoiler = edit.IsSpoiler;

        var thisReview = dbContext.Reviews.Where(r => r.Id == review.Id && r.ReviewerId == review.ReviewerId);
        await using (var transaction = await dbContext.Database.BeginTransactionAsync())
        {
            // A field not sent is set to itself.
            if (await thisReview.ExecuteUpdateAsync(s => s
                    .SetProperty(r => r.WritingQualityScore, r => writing ?? r.WritingQualityScore)
                    .SetProperty(r => r.UpdatingStabilityScore, r => stability ?? r.UpdatingStabilityScore)
                    .SetProperty(r => r.CharacterDevelopmentScore, r => characters ?? r.CharacterDevelopmentScore)
                    .SetProperty(r => r.WorldBuildingScore, r => world ?? r.WorldBuildingScore)
                    .SetProperty(r => r.Content, r => replacesContent ? content : r.Content)
                    .SetProperty(r => r.IsSpoiler, r => isSpoiler ?? r.IsSpoiler)
                    .SetProperty(r => r.UpdatedAt, (DateTime?)updatedAt)) == 0)
            {
                return false;
            }

            // The review's own average (Review.CalculateAverageScore's formula, which creating uses), from its four
            // scores as they are now, in SQL: the row stays locked from the first UPDATE to the commit, so a concurrent
            // edit waits and can't leave the average out of step with the scores. (Not in the first UPDATE: EF folds
            // the scores sent into one parameter typed like a score column, and 5 + 5 doesn't fit decimal(3,2).)
            await thisReview.ExecuteUpdateAsync(s => s.SetProperty(r => r.TotalAverageScore,
                r => (r.WritingQualityScore + r.UpdatingStabilityScore + r.CharacterDevelopmentScore + r.WorldBuildingScore) / 4));
            await transaction.CommitAsync();
        }

        // As after creating or deleting a review, after the commit: a changed score changes the novel's averages.
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

    public async Task<(IEnumerable<Review>, int)> GetNovelReviews(Guid novelId, int PageSize, int PageNumber, string sorting, string? viewerId = null)
    {
        var novelReviews = dbContext.Reviews
            .Where(n => n.NovelId == novelId)
            .VisibleTo(dbContext, viewerId)
            .Include(r => r.ReviewOwner)
            .AsQueryable();
        var totalCount = await novelReviews.CountAsync();
        if (PageNumber > 0 && PageSize > 0)
        {
            // A total order, so pages never repeat or skip a review: equal like counts (every review has 0 likes at
            // first) go newest first, and the id breaks the last ties.
            novelReviews = sorting?.ToLower() switch
            {
                "newest" => novelReviews.OrderByDescending(r => r.CreatedAt).ThenBy(r => r.Id),
                "oldest" => novelReviews.OrderBy(r => r.CreatedAt).ThenBy(r => r.Id),
                _ => novelReviews.OrderByDescending(r => r.LikeCount).ThenByDescending(r => r.CreatedAt).ThenBy(r => r.Id)
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

    public Task<Review?> GetReviewAsListedAsync(Guid reviewId) =>
        dbContext.Reviews
            .AsNoTracking()
            .Include(r => r.ReviewOwner)
            .FirstOrDefaultAsync(r => r.Id == reviewId);

    public async Task<Dictionary<Guid, Guid>> GetNovelIdsAsync(IReadOnlyCollection<Guid> reviewIds)
    {
        if (reviewIds.Count == 0)
        {
            return [];
        }

        return await dbContext.Reviews
            .AsNoTracking()
            .Where(r => reviewIds.Contains(r.Id))
            .Select(r => new { r.Id, r.NovelId })
            .ToDictionaryAsync(r => r.Id, r => r.NovelId);
    }

    public async Task<Review?> GetUserReviewForNovel(string userId, Guid novelId)
    {
        return await dbContext.Reviews.FirstOrDefaultAsync(r => r.ReviewerId == userId && r.NovelId == novelId);
    }
}
