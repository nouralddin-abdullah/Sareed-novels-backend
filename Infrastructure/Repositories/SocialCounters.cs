using Domain.Entities;
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
    /// Hard-deletes every comment on a chapter (its own and its paragraphs'), with all replies below them and their
    /// likes, and uncounts the still-visible ones from their authors. Comment likes and replies reference comments
    /// without a cascade, so a chapter or paragraph with liked or answered comments can't be deleted otherwise.
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
        DELETE c FROM Comments c JOIN @doomed x ON x.Id = c.Id;
        """;
}
