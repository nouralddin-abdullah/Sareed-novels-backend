using Application.Comments;
using Domain.Entities;
using Domain.Repositories;
using Infrastructure.Persistence;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Repositories;

public class CommentsRepository(ApplicationDbContext dbContext) : ICommentsRepository
{
    /// <summary>
    /// Saves the comment and counts it, in one transaction. A paragraph comment whose paragraph an edit removes at the
    /// same moment is a 404 <see cref="ParagraphGone"/>, not a server error: its insert either finds the paragraph
    /// gone (a foreign key violation) or loses a deadlock to the edit, which runs at a higher deadlock priority
    /// (ChapterParagraphsRepository.BeginEditAsync).
    /// </summary>
    public async Task<Comments> CreateComment(Comments Comment)
    {
        try
        {
            await using var transaction = await dbContext.Database.BeginTransactionAsync();
            dbContext.Comments.Add(Comment);
            await dbContext.SaveChangesAsync();
            await SocialCounters.AdjustForComment(dbContext, Comment, +1);
            await transaction.CommitAsync();
            return Comment;
        }
        catch (Exception ex) when (LostItsParagraph(Comment, ex))
        {
            dbContext.Entry(Comment).State = EntityState.Detached;
            throw ParagraphGone.Exception();
        }
    }

    /// <summary>SQL Server errors a paragraph comment's insert gets when an edit removes the paragraph meanwhile.</summary>
    internal const int ForeignKeyViolation = 547;

    internal const int DeadlockVictim = 1205;

    /// <summary>Whether <paramref name="ex"/> saving <paramref name="comment"/> means its paragraph was removed meanwhile.</summary>
    internal static bool LostItsParagraph(Comments comment, Exception ex) =>
        comment.ParagraphId is not null && SqlErrorNumber(ex) is ForeignKeyViolation or DeadlockVictim;

    /// <summary>
    /// The number of the first <see cref="SqlException"/> in <paramref name="ex"/>'s chain. The depth varies: the
    /// counter updates throw it as is, SaveChanges wraps it in a DbUpdateException, and EF's execution strategy wraps a
    /// transient error (a deadlock) once more, in an InvalidOperationException.
    /// </summary>
    private static int? SqlErrorNumber(Exception? ex)
    {
        for (; ex is not null; ex = ex.InnerException)
        {
            if (ex is SqlException sql)
            {
                return sql.Number;
            }
        }

        return null;
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

    public async Task<bool> RemoveCommentAsync(Guid commentId)
    {
        await using var transaction = await dbContext.Database.BeginTransactionAsync();
        // A comment its author deleted (soft) goes too, with the replies still under it.
        if (!await dbContext.Comments.IgnoreQueryFilters().AnyAsync(c => c.Id == commentId))
        {
            return false;
        }

        await SocialCounters.DeleteCommentTree(dbContext, commentId);
        await transaction.CommitAsync();
        return true;
    }

    public async Task<(IEnumerable<Comments>, int)> GetChapterComments(Guid chapterId, int pageNumber, int pageSize, string sorting = "recent", string? viewerId = null)
    {
        IQueryable<Comments> query = dbContext.Comments
            .Where(c => c.ChapterId == chapterId && c.ParentCommentId == null)
            .VisibleTo(dbContext, viewerId)
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

    public Task<Comments?> GetCommentAsListedAsync(Guid commentId) =>
        dbContext.Comments
            .AsNoTracking()
            .Include(c => c.User)
            .FirstOrDefaultAsync(c => c.Id == commentId);

    public Task<int> CountCommentsAheadAsync(Comments comment, string? viewerId = null)
    {
        // The same filters as GetCommentReplies and the Get*Comments lists.
        var comments = dbContext.Comments.VisibleTo(dbContext, viewerId);
        if (comment.ParentCommentId is { } parentId)
        {
            return comments.CountAsync(c => c.ParentCommentId == parentId && c.CreatedAt < comment.CreatedAt);
        }

        var newerTopLevel = comments.Where(c => c.ParentCommentId == null && c.CreatedAt > comment.CreatedAt);
        if (comment.ParagraphId is { } paragraphId)
        {
            return newerTopLevel.CountAsync(c => c.ParagraphId == paragraphId);
        }
        if (comment.ChapterId is { } chapterId)
        {
            return newerTopLevel.CountAsync(c => c.ChapterId == chapterId);
        }
        if (comment.PostId is { } postId)
        {
            return newerTopLevel.CountAsync(c => c.PostId == postId);
        }
        return Task.FromResult(0);
    }

    public Task<int> GetCommentCountForChapter(Guid chapterId)
    {
        return dbContext.Comments.CountAsync(c => c.ChapterId == chapterId && c.ParentCommentId == null);
    }

    public async Task<(IEnumerable<Comments>, int)> GetCommentReplies(Guid parentCommentId, int pageNumber, int PageSize, string sorting = "recent", string? viewerId = null)
    {
        IQueryable<Comments> query = dbContext.Comments
            .Where(c => c.ParentCommentId == parentCommentId)
            .VisibleTo(dbContext, viewerId)
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

    public async Task<Dictionary<Guid, int>> GetRepliesCounts(IEnumerable<Guid> commentIds, string? viewerId = null)
    {
        var ids = commentIds.ToList();
        if (ids.Count == 0)
        {
            return [];
        }

        return await dbContext.Comments
            .Where(c => c.ParentCommentId != null && ids.Contains(c.ParentCommentId.Value))
            .VisibleTo(dbContext, viewerId)
            .GroupBy(c => c.ParentCommentId!.Value)
            .Select(g => new { ParentId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.ParentId, x => x.Count);
    }

    public async Task<(IEnumerable<Comments>, int)> GetParagraphComments(Guid paragraphId, int pageNumber, int pageSize, string sorting = "recent", string? viewerId = null)
    {
        IQueryable<Comments> query = dbContext.Comments
            .Where(c => c.ParagraphId == paragraphId && c.ParentCommentId == null)
            .VisibleTo(dbContext, viewerId)
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

    public async Task<(IEnumerable<Comments>, int)> GetPostComments(Guid postId, int pageNumber, int pageSize, string sorting = "recent", string? viewerId = null)
    {
        IQueryable<Comments> query = dbContext.Comments
            .AsNoTracking()
            .Where(c => c.PostId == postId && c.ParentCommentId == null)
            .VisibleTo(dbContext, viewerId)
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
}
