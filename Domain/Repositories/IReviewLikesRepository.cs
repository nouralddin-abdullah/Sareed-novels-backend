using Domain.Entities;

namespace Domain.Repositories;

public interface IReviewLikesRepository
{
    /// <summary>Adds the like and bumps the review's LikeCount atomically; false when the user already liked it.</summary>
    Task<bool> LikeReview(string userId, Guid reviewId);
    /// <summary>Removes the like and lowers the review's LikeCount atomically; false when there was no like.</summary>
    Task<bool> UnLikeReview(string userId, Guid reviewId);
    Task<ReviewLike?> GetUserLikeForReview(string userId, Guid reviewId);
    Task<bool> HasUserLikedReview(string userId, Guid reviewId);
    Task<HashSet<Guid>> GetUserLikedReviewIds(string userId, IEnumerable<Guid> reviewIds);

}
