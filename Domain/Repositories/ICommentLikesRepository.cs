using Domain.Entities;

namespace Domain.Repositories;

public interface ICommentLikesRepository
{
    Task<bool> LikeComment(CommentLikes commentLike);
    Task<bool> UnLikeComment(string userId, Guid commentId);
    Task<CommentLikes?> GetUserLikeForComment(string userId, Guid commentId);
    Task<bool> HasUserLikedComment(string userId, Guid commentId);
    Task<HashSet<Guid>> GetUserLikedCommentIds(string userId, IEnumerable<Guid> commentIds);
    Task IncrementCommentLikesCount(Guid commentId);
    Task DecrementCommentLikesCount(Guid commentId);
}
