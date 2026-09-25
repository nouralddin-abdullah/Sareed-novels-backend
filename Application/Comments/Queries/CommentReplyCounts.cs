using Application.Comments.DTOS;
using Domain.Repositories;

namespace Application.Comments.Queries;

internal static class CommentReplyCounts
{
    /// <summary>Sets each comment's reply count from one grouped query for the whole page.</summary>
    public static async Task Fill(ICommentsRepository commentsRepository, List<CommentsDTO> comments)
    {
        var counts = await commentsRepository.GetRepliesCounts(comments.Select(c => c.Id));
        foreach (var comment in comments)
        {
            var repliesCount = counts.GetValueOrDefault(comment.Id);
            comment.TotalRepliesCount = repliesCount;
            comment.HasMoreReplies = repliesCount > 0;
        }
    }
}
