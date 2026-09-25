using Domain.Entities;

namespace Domain.Repositories;

public interface IPostLikesRepository
{
    /// <summary>Adds the like and bumps the post's LikesCount atomically; false when the user already liked it.</summary>
    Task<bool> LikePost(string userId, Guid postId);
    /// <summary>Removes the like and lowers the post's LikesCount atomically; false when there was no like.</summary>
    Task<bool> UnLikePost(string userId, Guid postId);
    Task<PostLike?> GetUserLikeForPost(string userId, Guid postId);
    Task<bool> HasUserLikedPost(string userId, Guid postId);
    Task<HashSet<Guid>> GetUserLikedPostIds(string userId, IEnumerable<Guid> postIds);
}
