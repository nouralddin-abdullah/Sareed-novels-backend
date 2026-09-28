using Application.Comments.DTOS;
using Domain.Repositories;

namespace Application.Comments.Queries;

internal static class CommentReplyCounts
{
    /// <summary>
    /// Sets each comment's reply count from one grouped query for the whole page, counting the replies the viewer's
    /// replies list shows (not those by users they blocked).
    /// </summary>
    public static async Task Fill(ICommentsRepository commentsRepository, List<CommentsDTO> comments, string? viewerId)
    {
        var counts = await commentsRepository.GetRepliesCounts(comments.Select(c => c.Id), viewerId);
        foreach (var comment in comments)
        {
            var repliesCount = counts.GetValueOrDefault(comment.Id);
            comment.TotalRepliesCount = repliesCount;
            comment.HasMoreReplies = repliesCount > 0;
        }
    }
}
