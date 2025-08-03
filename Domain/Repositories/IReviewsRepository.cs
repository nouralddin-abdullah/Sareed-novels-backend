using Domain.Entities;

namespace Domain.Repositories;

public interface IReviewsRepository
{
    Task<bool> CreateOne(Review review);
    Task<Review?> GetUserReviewForNovel(string userId, Guid novelId);
    Task<bool> DeleteReview(Review review);
    Task<bool> DeleteReviewWithNovelUpdate(Review review, Novel novel);
    Task<(IEnumerable<Review>, int)> GetNovelReviews(Guid novelId, int PageSize, int PageNumber, string sorting);
    Task<Review?> GetReviewById(Guid reviewId);
}
