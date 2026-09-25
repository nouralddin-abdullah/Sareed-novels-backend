using Application.Comments.DTOS;
using Application.Common;
using Application.Users;
using AutoMapper;
using Domain.Repositories;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Application.Comments.Queries.GetPostComments;

public class GetPostCommentsQueryHandler(
    ILogger<GetPostCommentsQueryHandler> logger,
    ICommentsRepository commentsRepository,
    ICommentLikesRepository commentLikesRepository,
    IUserContext userContext,
    IMapper mapper) : IRequestHandler<GetPostCommentsQuery, PagedResult<CommentsDTO>>
{
    public async Task<PagedResult<CommentsDTO>> Handle(GetPostCommentsQuery request, CancellationToken cancellationToken)
    {
        logger.LogInformation("Getting comments for post {PostId}, page {PageNumber}, sorting {Sorting}",
            request.PostId, request.PageNumber, request.Sorting);

        var (pageNumber, pageSize) = Paging.Clamp(request.PageNumber, request.PageSize);
        var (comments, totalCount) = await commentsRepository.GetPostComments(
            request.PostId,
            pageNumber,
            pageSize,
            request.Sorting);

        var commentDtos = mapper.Map<List<CommentsDTO>>(comments);
        await CommentReplyCounts.Fill(commentsRepository, commentDtos);

        var currentUser = userContext.GetCurrentUser();
        if (currentUser != null && commentDtos.Any())
        {
            var commentIds = commentDtos.Select(c => c.Id);
            var likedCommentIds = await commentLikesRepository.GetUserLikedCommentIds(currentUser.Id, commentIds);

            foreach (var commentDto in commentDtos)
            {
                commentDto.IsLikedByCurrentUser = likedCommentIds.Contains(commentDto.Id);
            }
        }

        return new PagedResult<CommentsDTO>(
            commentDtos,
            totalCount,
            pageSize,
            pageNumber);
    }
}
