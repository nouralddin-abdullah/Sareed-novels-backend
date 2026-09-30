using Domain.Entities;

namespace Domain.Repositories;

public interface IPostsRepository
{
    Task<Post> CreatePost(Post post);
    Task<Post?> GetPostById(Guid postId);
    /// <summary>The author of the post; null when there is no such post, or it was deleted.</summary>
    Task<string?> GetAuthorIdAsync(Guid postId, CancellationToken cancellationToken = default);
    Task<(IEnumerable<Post>, int)> GetUserPosts(string userId, int pageNumber, int pageSize);
    Task<bool> DeletePost(Guid postId);
}
