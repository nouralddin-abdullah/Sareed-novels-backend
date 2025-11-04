using Domain.Entities;

namespace Domain.Repositories;

public interface ICommentsRepository
{
    Task<Comments> CreateComment(Comments Comment);
    Task<(IEnumerable<Comments>, int)> GetChapterComments(Guid chapterId, int pageNumber, int pageSize, string sorting = "recent");
    Task<(IEnumerable<Comments>, int)> GetCommentReplies(Guid parentCommentId, int pageNumber, int PageSize, string sorting = "recent");
    Task<Comments?> GetCommentById(Guid commentId);
    Task<bool> DeleteComment(Guid commentId);
    Task<int> GetCommentCountForChapter(Guid chapterId);
    Task<int> GetRepliesCountForComment(Guid commentId);
    Task<(IEnumerable<Comments>, int)> GetParagraphComments(Guid paragraphId, int pageNumber, int pageSize, string sorting = "recent");
    Task DeleteParagraphComments(Guid paragraphId);
}
