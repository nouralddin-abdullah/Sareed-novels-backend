using Domain.Entities;

namespace Domain.Repositories;

public interface ICommentsRepository
{
    /// <summary>Saves the comment and bumps its author's and its post/chapter/paragraph's counters in one transaction.</summary>
    Task<Comments> CreateComment(Comments Comment);
    Task<(IEnumerable<Comments>, int)> GetChapterComments(Guid chapterId, int pageNumber, int pageSize, string sorting = "recent");
    Task<(IEnumerable<Comments>, int)> GetParagraphComments(Guid paragraphId, int pageNumber, int pageSize, string sorting = "recent");
    Task<(IEnumerable<Comments>, int)> GetPostComments(Guid postId, int pageNumber, int pageSize, string sorting = "recent");
    Task<(IEnumerable<Comments>, int)> GetCommentReplies(Guid parentCommentId, int pageNumber, int PageSize, string sorting = "recent");
    Task<Comments?> GetCommentById(Guid commentId);
    /// <summary>Soft-deletes the comment and lowers the counters CreateComment raised, in one transaction.</summary>
    Task<bool> DeleteComment(Guid commentId);
    Task<int> GetCommentCountForChapter(Guid chapterId);
    /// <summary>Visible replies per comment, for a page of comments, in one grouped query.</summary>
    Task<Dictionary<Guid, int>> GetRepliesCounts(IEnumerable<Guid> commentIds);
    /// <summary>Removes a paragraph's comments (with replies and likes) so the paragraph itself can be deleted.</summary>
    Task DeleteParagraphComments(Guid paragraphId);
}
