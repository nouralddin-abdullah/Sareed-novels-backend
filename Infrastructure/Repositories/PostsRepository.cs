using Domain.Entities;
using Domain.Repositories;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Repositories;

public class PostsRepository(ApplicationDbContext dbContext) : IPostsRepository
{
    public async Task<Post> CreatePost(Post post)
    {
        dbContext.Posts.Add(post);
        await dbContext.SaveChangesAsync();
        return post;
    }

    public async Task<bool> DeletePost(Guid postId)
    {
        var post = await dbContext.Posts.FindAsync(postId);
        if (post == null) return false;
        post.IsDeleted = true;
        return await dbContext.SaveChangesAsync() > 0;
    }

    public async Task<Post?> GetPostById(Guid postId)
    {
        return await dbContext.Posts
            .AsNoTracking()
            .Include(p => p.User)
            .Include(p => p.Novel)
            .FirstOrDefaultAsync(p => p.Id == postId);
    }

    public async Task<(IEnumerable<Post>, int)> GetUserPosts(string userId, int pageNumber, int pageSize)
    {
        var query = dbContext.Posts
            .AsNoTracking()
            .Where(p => p.UserId == userId)
            .Include(p => p.User)
            .Include(p => p.Novel);

        var totalCount = await query.CountAsync();
        
        var posts = await query
            .OrderByDescending(p => p.CreatedAt)
            .Skip((pageNumber - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        return (posts, totalCount);
    }

    public async Task<bool> UpdatePost(Post post)
    {
        dbContext.Posts.Update(post);
        return await dbContext.SaveChangesAsync() > 0;
    }
}
