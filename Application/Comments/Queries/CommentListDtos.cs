using Application.Comments.DTOS;
using Application.Users;
using AutoMapper;
using Domain.Repositories;

namespace Application.Comments.Queries;

/// <summary>
/// Comments as the chapter, paragraph and post comment lists return them: mapped, with their reply counts and whether
/// the signed-in reader liked each. Creating a comment returns the new one through here too, so the two can't differ.
/// </summary>
internal static class CommentListDtos
{
    public static async Task<List<CommentsDTO>> Build(
        IEnumerable<Domain.Entities.Comments> comments,
        IMapper mapper,
        ICommentsRepository commentsRepository,
        ICommentLikesRepository commentLikesRepository,
        CurrentUser? currentUser)
    {
        var commentDtos = mapper.Map<List<CommentsDTO>>(comments);
        await CommentReplyCounts.Fill(commentsRepository, commentDtos);

        if (currentUser != null && commentDtos.Count > 0)
        {
            var likedCommentIds = await commentLikesRepository.GetUserLikedCommentIds(currentUser.Id, commentDtos.Select(c => c.Id));
            foreach (var commentDto in commentDtos)
            {
                commentDto.IsLikedByCurrentUser = likedCommentIds.Contains(commentDto.Id);
            }
        }

        return commentDtos;
    }
}
