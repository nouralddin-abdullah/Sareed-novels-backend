using Domain.Entities;
using Domain.Reviews;

namespace Domain.Repositories;

public interface IReviewsRepository
{
    /// <summary>Saves the review, counts it for its author and recomputes the novel's review stats.</summary>
    Task<bool> CreateOne(Review review);
    Task<Review?> GetUserReviewForNovel(string userId, Guid novelId);
    /// <summary>Deletes the review with its likes, uncounts it for its author and recomputes the novel's review stats.</summary>
    Task<bool> DeleteReview(Review review);
    /// <summary>
    /// Applies its author's edit to the review (#34) in one transaction: the fields sent, UpdatedAt, and the review's own
    /// average of its four scores as they end up, computed in SQL. Then recomputes the novel's review stats, as creating
    /// and deleting do. False when the review is gone.
    /// </summary>
    Task<bool> UpdateReview(Review review, ReviewEdit edit, DateTime updatedAt);
    /// <summary>Recomputes ReviewCount and the score averages of a novel from its reviews in one SQL statement.</summary>
    Task RefreshNovelReviewStats(Guid novelId);
    /// <summary>A page of the novel's reviews; reviews by users the viewer blocked are left out (and not counted).</summary>
    Task<(IEnumerable<Review>, int)> GetNovelReviews(Guid novelId, int PageSize, int PageNumber, string sorting, string? viewerId = null);
    Task<Review?> GetReviewById(Guid reviewId);
    /// <summary>
    /// A review with its reviewer, read from the database as <see cref="GetNovelReviews"/> reads them (untracked), so a
    /// review just created serializes exactly as it will in the list.
    /// </summary>
    Task<Review?> GetReviewAsListedAsync(Guid reviewId);
    /// <summary>The novel each of these reviews is on, for the reviews that still exist.</summary>
    Task<Dictionary<Guid, Guid>> GetNovelIdsAsync(IReadOnlyCollection<Guid> reviewIds);
}
