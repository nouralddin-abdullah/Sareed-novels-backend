using Domain.Entities;

namespace Domain.Repositories;

public interface ICommentsRepository
{
    /// <summary>Saves the comment and bumps its author's and its post/chapter/paragraph's counters in one transaction.</summary>
    Task<Comments> CreateComment(Comments Comment);
    // The lists take the signed-in viewer (null when anonymous): comments by users the viewer blocked are left out,
    // and the total counts only what the viewer can list.
    Task<(IEnumerable<Comments>, int)> GetChapterComments(Guid chapterId, int pageNumber, int pageSize, string sorting = "recent", string? viewerId = null);
    Task<(IEnumerable<Comments>, int)> GetParagraphComments(Guid paragraphId, int pageNumber, int pageSize, string sorting = "recent", string? viewerId = null);
    Task<(IEnumerable<Comments>, int)> GetPostComments(Guid postId, int pageNumber, int pageSize, string sorting = "recent", string? viewerId = null);
    Task<(IEnumerable<Comments>, int)> GetCommentReplies(Guid parentCommentId, int pageNumber, int PageSize, string sorting = "recent", string? viewerId = null);
    Task<Comments?> GetCommentById(Guid commentId);
    /// <summary>
    /// A comment with its author, read from the database as the comment lists read theirs (untracked), so a comment
    /// just created serializes exactly as it will in a list.
    /// </summary>
    Task<Comments?> GetCommentAsListedAsync(Guid commentId);
    /// <summary>
    /// How many comments come before this one in the list that shows it, in that list's default order: newer top-level
    /// comments on the same paragraph, chapter or post (those lists are newest first), or earlier replies to the same
    /// comment (replies are oldest first). Comments by users the viewer blocked aren't listed, so they don't count.
    /// </summary>
    Task<int> CountCommentsAheadAsync(Comments comment, string? viewerId = null);
    /// <summary>Soft-deletes the comment and lowers the counters CreateComment raised, in one transaction.</summary>
    Task<bool> DeleteComment(Guid commentId);
    /// <summary>
    /// Moderation: hard-deletes the comment with every reply below it, their likes and the notifications about them,
    /// and lowers the counters they held (their authors', and the post's, paragraph's or chapter's for a visible
    /// top-level comment), in one transaction. False when there was no such comment.
    /// </summary>
    Task<bool> RemoveCommentAsync(Guid commentId);
    Task<int> GetCommentCountForChapter(Guid chapterId);
    /// <summary>
    /// Visible replies per comment, for a page of comments, in one grouped query; replies by users the viewer blocked
    /// aren't counted, as the replies list leaves them out.
    /// </summary>
    Task<Dictionary<Guid, int>> GetRepliesCounts(IEnumerable<Guid> commentIds, string? viewerId = null);
    /// <summary>Removes a paragraph's comments (with replies and likes) so the paragraph itself can be deleted.</summary>
    Task DeleteParagraphComments(Guid paragraphId);
}
