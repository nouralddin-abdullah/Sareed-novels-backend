using Domain.Entities;
using Domain.Repositories;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Repositories;

public class CommentLikesRepository(ApplicationDbContext dbContext) : ICommentLikesRepository
{
    public async Task<HashSet<Guid>> GetUserLikedCommentIds(string userId, IEnumerable<Guid> commentIds)
    {
        var likedCommentIds = await dbContext.CommentLikes
            .Where(cl => cl.UserId == userId && commentIds.Contains(cl.CommentId))
            .Select(cl => cl.CommentId)
            .ToListAsync();

        return new HashSet<Guid>(likedCommentIds);
    }

    public async Task<CommentLikes?> GetUserLikeForComment(string userId, Guid commentId)
    {
        return await dbContext.CommentLikes
            .FirstOrDefaultAsync(cl => cl.UserId == userId && cl.CommentId == commentId);
    }

    public async Task<bool> HasUserLikedComment(string userId, Guid commentId)
    {
        return await dbContext.CommentLikes
            .AnyAsync(cl => cl.UserId == userId && cl.CommentId == commentId);
    }

    public async Task<bool> LikeComment(CommentLikes commentLike)
    {
            await dbContext.CommentLikes.AddAsync(commentLike);
            return await dbContext.SaveChangesAsync() > 0;
    }

    public async Task<bool> UnLikeComment(string userId, Guid commentId)
    {
        var existingLike = await dbContext.CommentLikes
            .FirstOrDefaultAsync(cl => cl.UserId == userId && cl.CommentId == commentId);

        if (existingLike == null)
            return false;

        dbContext.CommentLikes.Remove(existingLike);
        return await dbContext.SaveChangesAsync() > 0;
    }

    public async Task IncrementCommentLikesCount(Guid commentId)
    {
        var comment = await dbContext.Comments.FindAsync(commentId);
        if (comment != null)
        {
            comment.IncrementLikeCount();
            await dbContext.SaveChangesAsync();
        }
    }

    public async Task DecrementCommentLikesCount(Guid commentId)
    {
        var comment = await dbContext.Comments.FindAsync(commentId);
        if (comment != null)
        {
            comment.DecrementLikeCount();
            await dbContext.SaveChangesAsync();
        }
    }
}