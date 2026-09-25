using Domain.Entities;
using Domain.Repositories;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Repositories;

public class CommentsRepository(ApplicationDbContext dbContext) : ICommentsRepository
{
    public async Task<Comments> CreateComment(Comments Comment)
    {
        await using var transaction = await dbContext.Database.BeginTransactionAsync();
        dbContext.Comments.Add(Comment);
        await dbContext.SaveChangesAsync();
        await SocialCounters.AdjustForComment(dbContext, Comment, +1);
        await transaction.CommitAsync();
        return Comment;
    }

    public async Task<bool> DeleteComment(Guid commentId)
    {
        await using var transaction = await dbContext.Database.BeginTransactionAsync();
        // The query filter limits this to a comment that is still visible, so a repeated delete uncounts nothing.
        var deleted = await dbContext.Comments
            .Where(c => c.Id == commentId)
            .ExecuteUpdateAsync(s => s.SetProperty(c => c.IsDeleted, true));
        if (deleted == 0)
        {
            return false;
        }

        var comment = await dbContext.Comments
            .IgnoreQueryFilters()
            .AsNoTracking()
            .SingleAsync(c => c.Id == commentId);
        await SocialCounters.AdjustForComment(dbContext, comment, -1);
        await transaction.CommitAsync();
        return true;
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

    public async Task<Dictionary<Guid, int>> GetRepliesCounts(IEnumerable<Guid> commentIds)
    {
        var ids = commentIds.ToList();
        if (ids.Count == 0)
        {
            return [];
        }

        return await dbContext.Comments
            .Where(c => c.ParentCommentId != null && ids.Contains(c.ParentCommentId.Value))
            .GroupBy(c => c.ParentCommentId!.Value)
            .Select(g => new { ParentId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.ParentId, x => x.Count);
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

    public async Task<(IEnumerable<Comments>, int)> GetPostComments(Guid postId, int pageNumber, int pageSize, string sorting = "recent")
    {
        IQueryable<Comments> query = dbContext.Comments
            .AsNoTracking()
            .Where(c => c.PostId == postId && c.ParentCommentId == null)
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
        // The paragraph row is deleted right after this, and comments reference it without a cascade (as likes and
        // replies reference comments), so a soft delete would leave it undeletable.
        await using var transaction = await dbContext.Database.BeginTransactionAsync();
        await SocialCounters.DeleteParagraphComments(dbContext, paragraphId);
        await transaction.CommitAsync();
    }
}
