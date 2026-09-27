using System.Data;
using Domain.Entities;
using Domain.Repositories;
using Infrastructure.Persistence;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Repositories;

/// <summary>
/// Counter maintenance for the social features. Every change is one atomic SQL UPDATE (<c>x = x + 1</c>) run inside
/// the request, never a read-modify-write of a loaded entity, so concurrent requests cannot lose increments and
/// no unrelated columns or rows get rewritten.
/// </summary>
internal static class SocialCounters
{
    /// <summary>
    /// Moves the counters a comment contributes to by <paramref name="delta"/> (+1 on create, -1 on delete): its
    /// author's CommentsCount always, and for top-level comments the post's CommentsCount, or the paragraph's
    /// CommentsCount plus its chapter's TotalCommentsCount, or the chapter's CommentsCount and TotalCommentsCount.
    /// </summary>
    public static async Task AdjustForComment(ApplicationDbContext db, Comments comment, int delta)
    {
        await db.Users
            .Where(u => u.Id == comment.UserId)
            .ExecuteUpdateAsync(s => s.SetProperty(u => u.CommentsCount,
                u => u.CommentsCount + delta < 0 ? 0 : u.CommentsCount + delta));

        if (comment.ParentCommentId.HasValue)
        {
            return;
        }

        if (comment.PostId is { } postId)
        {
            await db.Posts
                .IgnoreQueryFilters()
                .Where(p => p.Id == postId)
                .ExecuteUpdateAsync(s => s.SetProperty(p => p.CommentsCount,
                    p => p.CommentsCount + delta < 0 ? 0 : p.CommentsCount + delta));
        }
        else if (comment.ParagraphId is { } paragraphId)
        {
            await db.ChapterParagraphs
                .Where(p => p.Id == paragraphId)
                .ExecuteUpdateAsync(s => s.SetProperty(p => p.CommentsCount,
                    p => p.CommentsCount + delta < 0 ? 0 : p.CommentsCount + delta));
            await db.Chapters
                .Where(c => db.ChapterParagraphs.Any(p => p.Id == paragraphId && p.ChapterId == c.Id))
                .ExecuteUpdateAsync(s => s.SetProperty(c => c.TotalCommentsCount,
                    c => c.TotalCommentsCount + delta < 0 ? 0 : c.TotalCommentsCount + delta));
        }
        else if (comment.ChapterId is { } chapterId)
        {
            await db.Chapters
                .Where(c => c.Id == chapterId)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(c => c.CommentsCount, c => c.CommentsCount + delta < 0 ? 0 : c.CommentsCount + delta)
                    .SetProperty(c => c.TotalCommentsCount,
                        c => c.TotalCommentsCount + delta < 0 ? 0 : c.TotalCommentsCount + delta));
        }
    }

    /// <summary>
    /// Hard-deletes every comment on a chapter (its own and its paragraphs'), with all replies below them, their
    /// likes and the notifications about them, and uncounts the still-visible ones from their authors. Comment likes
    /// and replies reference comments without a cascade, so a chapter or paragraph with liked or answered comments
    /// can't be deleted otherwise.
    /// </summary>
    public static Task DeleteChapterComments(ApplicationDbContext db, Guid chapterId) =>
        db.Database.ExecuteSqlRawAsync(
            DoomedComments("c.ChapterId = @id OR c.ParagraphId IN (SELECT p.Id FROM ChapterParagraphs p WHERE p.ChapterId = @id)")
            + DeleteDoomed,
            new SqlParameter("@id", chapterId));

    /// <summary>
    /// Same as <see cref="DeleteChapterComments"/> for one paragraph that is about to be removed; the chapter's
    /// TotalCommentsCount loses the paragraph's visible top-level comments.
    /// </summary>
    public static Task DeleteParagraphComments(ApplicationDbContext db, Guid paragraphId) =>
        db.Database.ExecuteSqlRawAsync(
            DoomedComments("c.ParagraphId = @id")
            + """
              UPDATE ch SET TotalCommentsCount = CASE WHEN ch.TotalCommentsCount > n.Cnt THEN ch.TotalCommentsCount - n.Cnt ELSE 0 END
              FROM Chapters ch
              JOIN ChapterParagraphs p ON p.ChapterId = ch.Id AND p.Id = @id
              CROSS APPLY (SELECT COUNT(*) AS Cnt FROM Comments c
                           WHERE c.ParagraphId = @id AND c.ParentCommentId IS NULL AND c.IsDeleted = 0) n
              WHERE n.Cnt > 0;

              """
            + DeleteDoomed,
            new SqlParameter("@id", paragraphId));

    /// <summary>
    /// Same as <see cref="DeleteChapterComments"/> for the paragraphs a chapter edit removes, which the caller has
    /// marked with a negative OrderIndex inside its transaction: their comments go with replies, likes and
    /// notifications, and the chapter's TotalCommentsCount loses their visible top-level comments.
    /// </summary>
    public static async Task<RemovedParagraphComments> DeleteCommentsOnRemovedParagraphs(ApplicationDbContext db, Guid chapterId)
    {
        var comments = new SqlParameter("@comments", SqlDbType.Int) { Direction = ParameterDirection.Output };
        var visible = new SqlParameter("@visible", SqlDbType.Int) { Direction = ParameterDirection.Output };
        await db.Database.ExecuteSqlRawAsync(RemovedParagraphCommentsSql, new SqlParameter("@id", chapterId), comments, visible);
        return new RemovedParagraphComments((int)comments.Value, (int)visible.Value);
    }

    /// <summary>
    /// Moderation: hard-deletes one comment with every reply below it, their likes and the notifications about them,
    /// and uncounts the still-visible ones from their authors (as <see cref="DeleteChapterComments"/> does). A visible
    /// top-level comment is also uncounted where <see cref="AdjustForComment"/> counted it: its post, or its paragraph
    /// and that paragraph's chapter, or its chapter. The caller runs it inside a transaction.
    /// </summary>
    public static Task DeleteCommentTree(ApplicationDbContext db, Guid commentId) =>
        db.Database.ExecuteSqlRawAsync(
            DoomedComments("c.Id = @id")
            + """
              DECLARE @postId uniqueidentifier, @paragraphId uniqueidentifier, @chapterId uniqueidentifier;
              SELECT @postId = c.PostId, @paragraphId = c.ParagraphId, @chapterId = c.ChapterId
              FROM Comments c
              WHERE c.Id = @id AND c.ParentCommentId IS NULL AND c.IsDeleted = 0;

              IF @postId IS NOT NULL
                  UPDATE Posts SET CommentsCount = CASE WHEN CommentsCount > 0 THEN CommentsCount - 1 ELSE 0 END
                  WHERE Id = @postId;
              ELSE IF @paragraphId IS NOT NULL
              BEGIN
                  UPDATE ChapterParagraphs SET CommentsCount = CASE WHEN CommentsCount > 0 THEN CommentsCount - 1 ELSE 0 END
                  WHERE Id = @paragraphId;
                  UPDATE ch SET TotalCommentsCount = CASE WHEN ch.TotalCommentsCount > 0 THEN ch.TotalCommentsCount - 1 ELSE 0 END
                  FROM Chapters ch JOIN ChapterParagraphs p ON p.ChapterId = ch.Id
                  WHERE p.Id = @paragraphId;
              END
              ELSE IF @chapterId IS NOT NULL
                  UPDATE Chapters SET CommentsCount = CASE WHEN CommentsCount > 0 THEN CommentsCount - 1 ELSE 0 END,
                                      TotalCommentsCount = CASE WHEN TotalCommentsCount > 0 THEN TotalCommentsCount - 1 ELSE 0 END
                  WHERE Id = @chapterId;

              """
            + DeleteDoomed,
            new SqlParameter("@id", commentId));

    private const string RemovedParagraphCommentsSql = """
        DECLARE @doomed TABLE (Id uniqueidentifier PRIMARY KEY);
        WITH tree AS (
            SELECT c.Id FROM Comments c
            JOIN ChapterParagraphs p ON p.Id = c.ParagraphId
            WHERE p.ChapterId = @id AND p.OrderIndex < 0
            UNION ALL
            SELECT r.Id FROM Comments r JOIN tree t ON r.ParentCommentId = t.Id
        )
        INSERT INTO @doomed (Id) SELECT DISTINCT Id FROM tree;

        SELECT @comments = COUNT(*), @visible = COUNT(CASE WHEN c.IsDeleted = 0 THEN 1 END)
        FROM Comments c JOIN @doomed x ON x.Id = c.Id;

        UPDATE ch SET TotalCommentsCount = CASE WHEN ch.TotalCommentsCount > n.Cnt THEN ch.TotalCommentsCount - n.Cnt ELSE 0 END
        FROM Chapters ch
        CROSS APPLY (SELECT COUNT(*) AS Cnt FROM Comments c
                     JOIN ChapterParagraphs p ON p.Id = c.ParagraphId
                     WHERE p.ChapterId = @id AND p.OrderIndex < 0 AND c.ParentCommentId IS NULL AND c.IsDeleted = 0) n
        WHERE ch.Id = @id AND n.Cnt > 0;

        """ + DeleteDoomed;

    private static string DoomedComments(string rootPredicate) => $"""
        DECLARE @doomed TABLE (Id uniqueidentifier PRIMARY KEY);
        WITH tree AS (
            SELECT c.Id FROM Comments c WHERE {rootPredicate}
            UNION ALL
            SELECT r.Id FROM Comments r JOIN tree t ON r.ParentCommentId = t.Id
        )
        INSERT INTO @doomed (Id) SELECT DISTINCT Id FROM tree;

        """;

    private const string DeleteDoomed = """
        UPDATE u SET CommentsCount = CASE WHEN u.CommentsCount > d.Cnt THEN u.CommentsCount - d.Cnt ELSE 0 END
        FROM AspNetUsers u
        JOIN (SELECT c.UserId, COUNT(*) AS Cnt FROM Comments c JOIN @doomed x ON x.Id = c.Id
              WHERE c.IsDeleted = 0 GROUP BY c.UserId) d ON d.UserId = u.Id;

        DELETE l FROM CommentLikes l JOIN @doomed x ON x.Id = l.CommentId;
        DELETE n FROM Notifications n JOIN @doomed x ON x.Id = n.RelatedEntityId WHERE n.RelatedEntityType = N'Comment';
        DELETE c FROM Comments c JOIN @doomed x ON x.Id = c.Id;
        """;
}
