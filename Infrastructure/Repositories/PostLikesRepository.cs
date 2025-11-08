using Domain.Entities;
using Domain.Repositories;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Repositories;

public class PostLikesRepository(ApplicationDbContext dbContext) : IPostLikesRepository
{
    public async Task<PostLike?> GetUserLikeForPost(string userId, Guid postId)
    {
        return await dbContext.PostLikes
            .AsNoTracking()
            .FirstOrDefaultAsync(pl => pl.UserId == userId && pl.PostId == postId);
    }

    public async Task<HashSet<Guid>> GetUserLikedPostIds(string userId, IEnumerable<Guid> postIds)
    {
        var postIdsList = postIds as List<Guid> ?? postIds.ToList();
        
        var likedPostIds = await dbContext.PostLikes
            .AsNoTracking()
            .Where(pl => pl.UserId == userId && postIdsList.Contains(pl.PostId))
            .Select(pl => pl.PostId)
            .ToListAsync();
        
        return new HashSet<Guid>(likedPostIds);
    }

    public async Task<bool> HasUserLikedPost(string userId, Guid postId)
    {
        return await dbContext.PostLikes
            .AsNoTracking()
            .AnyAsync(pl => pl.UserId == userId && pl.PostId == postId);
    }

    public async Task<bool> LikePost(PostLike postLike)
    {
        dbContext.PostLikes.Add(postLike);
        return await dbContext.SaveChangesAsync() > 0;
    }

    public async Task<bool> UnLikePost(string userId, Guid postId)
    {
        var postLike = await dbContext.PostLikes
            .FirstOrDefaultAsync(pl => pl.UserId == userId && pl.PostId == postId);
        
        if (postLike == null) return false;
        
        dbContext.PostLikes.Remove(postLike);
        return await dbContext.SaveChangesAsync() > 0;
    }
}
