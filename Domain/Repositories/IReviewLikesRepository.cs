using Domain.Entities;

namespace Domain.Repositories;

public interface IReviewLikesRepository
{
    Task<bool> LikeReview(ReviewLike reviewLike);
    Task<bool> UnLikeReview(string userId, Guid reviewLike);
    Task<ReviewLike?> GetUserLikeForReview(string userId, Guid reviewId);
    Task<bool> HasUserLikedReview(string userId, Guid reviewId);
    Task<HashSet<Guid>> GetUserLikedReviewIds(string userId, IEnumerable<Guid> reviewIds);

}
