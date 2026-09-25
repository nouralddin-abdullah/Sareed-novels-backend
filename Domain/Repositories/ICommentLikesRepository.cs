using Domain.Entities;

namespace Domain.Repositories;

public interface ICommentLikesRepository
{
    /// <summary>Adds the like and bumps the comment's LikesCount atomically; false when the user already liked it.</summary>
    Task<bool> LikeComment(string userId, Guid commentId);
    /// <summary>Removes the like and lowers the comment's LikesCount atomically; false when there was no like.</summary>
    Task<bool> UnLikeComment(string userId, Guid commentId);
    Task<CommentLikes?> GetUserLikeForComment(string userId, Guid commentId);
    Task<bool> HasUserLikedComment(string userId, Guid commentId);
    Task<HashSet<Guid>> GetUserLikedCommentIds(string userId, IEnumerable<Guid> commentIds);
}
