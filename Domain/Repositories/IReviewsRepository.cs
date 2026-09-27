using Domain.Entities;

namespace Domain.Repositories;

public interface IReviewsRepository
{
    /// <summary>Saves the review, counts it for its author and recomputes the novel's review stats.</summary>
    Task<bool> CreateOne(Review review);
    Task<Review?> GetUserReviewForNovel(string userId, Guid novelId);
    /// <summary>Deletes the review with its likes, uncounts it for its author and recomputes the novel's review stats.</summary>
    Task<bool> DeleteReview(Review review);
    /// <summary>Recomputes ReviewCount and the score averages of a novel from its reviews in one SQL statement.</summary>
    Task RefreshNovelReviewStats(Guid novelId);
    Task<(IEnumerable<Review>, int)> GetNovelReviews(Guid novelId, int PageSize, int PageNumber, string sorting);
    Task<Review?> GetReviewById(Guid reviewId);
    /// <summary>
    /// A review with its reviewer, read from the database as <see cref="GetNovelReviews"/> reads them (untracked), so a
    /// review just created serializes exactly as it will in the list.
    /// </summary>
    Task<Review?> GetReviewAsListedAsync(Guid reviewId);
    /// <summary>The novel each of these reviews is on, for the reviews that still exist.</summary>
    Task<Dictionary<Guid, Guid>> GetNovelIdsAsync(IReadOnlyCollection<Guid> reviewIds);
}
