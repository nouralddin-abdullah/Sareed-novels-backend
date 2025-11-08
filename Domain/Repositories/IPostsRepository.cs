using Domain.Entities;

namespace Domain.Repositories;

public interface IPostsRepository
{
    Task<Post> CreatePost(Post post);
    Task<Post?> GetPostById(Guid postId);
    Task<(IEnumerable<Post>, int)> GetUserPosts(string userId, int pageNumber, int pageSize);
    Task<bool> DeletePost(Guid postId);
    Task<bool> UpdatePost(Post post);
}
