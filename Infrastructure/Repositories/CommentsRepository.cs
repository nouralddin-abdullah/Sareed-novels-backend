using Domain.Entities;
using Domain.Repositories;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Repositories;

public class CommentsRepository(ApplicationDbContext dbContext) : ICommentsRepository
{
    public async Task<Comments> CreateComment(Comments Comment)
    {
        dbContext.Comments.Add(Comment);
        await dbContext.SaveChangesAsync();
        return Comment;
    }

    public async Task<bool> DeleteComment(Guid commentId)
    {
        var comment = await dbContext.Comments.FindAsync(commentId);
        if (comment == null) return false;
        comment.IsDeleted = true;
        return await dbContext.SaveChangesAsync() > 0;
    }

    public async Task<(IEnumerable<Comments>, int)> GetChapterComments(Guid chapterId, int pageNumber, int pageSize, string sorting = "recent")
    {
        IQueryable<Comments> query = dbContext.Comments
            .Where(c => c.ChapterId == chapterId && c.ParentCommentId == null)
            .Include(c => c.User);
        query = sorting.ToLower() switch
        {
            "oldest" => query.OrderBy(c => c.CreatedAt),
            "mostliked" or "most-liked" or "popular" => query.OrderByDescending(c => c.LikesCount)
                                                               .ThenByDescending(c => c.CreatedAt),
            _ => query.OrderByDescending(c => c.CreatedAt)
        };

        var totalCount = await query.CountAsync();
        var comments = await query
            .Skip((pageNumber - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        return (comments, totalCount);
    }

    public async Task<Comments?> GetCommentById(Guid commentId)
    {
        return await dbContext.Comments
            .Include(c => c.User)
            .FirstOrDefaultAsync(c => c.Id == commentId);
    }

    public Task<int> GetCommentCountForChapter(Guid chapterId)
    {
        return dbContext.Comments.CountAsync(c => c.ChapterId == chapterId && c.ParentCommentId == null);
    }

    public async Task<(IEnumerable<Comments>, int)> GetCommentReplies(Guid parentCommentId, int pageNumber, int PageSize, string sorting = "recent")
    {
        IQueryable<Comments> query = dbContext.Comments
            .Where(c => c.ParentCommentId == parentCommentId)
            .Include(c => c.User);

        query = sorting.ToLower() switch
        {
            "recent" => query.OrderByDescending(c => c.CreatedAt),
            "mostliked" => query.OrderByDescending(c => c.LikesCount)
                                                               .ThenBy(c => c.CreatedAt),
            _ => query.OrderBy(c => c.CreatedAt)
        };

        var totalCount = await query.CountAsync();

        var replies = await query
            .Skip((pageNumber - 1) * PageSize)
            .Take(PageSize)
            .ToListAsync();

        return (replies, totalCount);
    }

    public async Task<int> GetRepliesCountForComment(Guid commentId)
    {
        return await dbContext.Comments
            .CountAsync(c => c.ParentCommentId == commentId);
    }

    public async Task<(IEnumerable<Comments>, int)> GetParagraphComments(Guid paragraphId, int pageNumber, int pageSize, string sorting = "recent")
    {
        IQueryable<Comments> query = dbContext.Comments
            .Where(c => c.ParagraphId == paragraphId && c.ParentCommentId == null)
            .Include(c => c.User);

        query = sorting.ToLower() switch
        {
            "oldest" => query.OrderBy(c => c.CreatedAt),
            "mostliked" or "most-liked" or "popular" => query.OrderByDescending(c => c.LikesCount)
                                                               .ThenByDescending(c => c.CreatedAt),
            _ => query.OrderByDescending(c => c.CreatedAt)
        };

        var totalCount = await query.CountAsync();
        
        var comments = await query
            .Skip((pageNumber - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        return (comments, totalCount);
    }
    
    public async Task DeleteParagraphComments(Guid paragraphId)
    {
        // Get all comments for this paragraph (including replies via navigation)
        var paragraphComments = await dbContext.Comments
            .Where(c => c.ParagraphId == paragraphId)
            .ToListAsync();
        
        if (paragraphComments.Any())
        {
            // Mark all as deleted (soft delete)
            foreach (var comment in paragraphComments)
            {
                comment.IsDeleted = true;
            }
            
            await dbContext.SaveChangesAsync();
        }
    }
}
