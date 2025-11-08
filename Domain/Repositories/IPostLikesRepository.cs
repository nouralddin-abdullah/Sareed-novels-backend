using Domain.Entities;

namespace Domain.Repositories;

public interface IPostLikesRepository
{
    Task<bool> LikePost(PostLike postLike);
    Task<bool> UnLikePost(string userId, Guid postId);
    Task<PostLike?> GetUserLikeForPost(string userId, Guid postId);
    Task<bool> HasUserLikedPost(string userId, Guid postId);
    Task<HashSet<Guid>> GetUserLikedPostIds(string userId, IEnumerable<Guid> postIds);
}
