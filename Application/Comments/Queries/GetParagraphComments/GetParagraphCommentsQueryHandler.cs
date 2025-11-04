using Application.Comments.DTOS;
using Application.Common;
using Application.Users;
using AutoMapper;
using Domain.Repositories;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Application.Comments.Queries.GetParagraphComments;

public class GetParagraphCommentsQueryHandler(ILogger<GetParagraphCommentsQueryHandler> logger, ICommentsRepository commentsRepository, ICommentLikesRepository commentLikesRepository, IUserContext userContext, IMapper mapper) : IRequestHandler<GetParagraphCommentsQuery, PagedResult<CommentsDTO>>
{
    public async Task<PagedResult<CommentsDTO>> Handle(GetParagraphCommentsQuery request, CancellationToken cancellationToken)
    {
        logger.LogInformation("Getting comments for paragraph {ParagraphId}, page {PageNumber}, sorting {Sorting}", 
            request.ParagraphId, request.PageNumber, request.Sorting);

        var (comments, totalCount) = await commentsRepository.GetParagraphComments(
            request.ParagraphId,
            request.PageNumber,
            request.PageSize,
            request.Sorting);

        var commentDtos = mapper.Map<List<CommentsDTO>>(comments);

        foreach (var commentDto in commentDtos)
        {
            var repliesCount = await commentsRepository.GetRepliesCountForComment(commentDto.Id);
            commentDto.TotalRepliesCount = repliesCount;
            commentDto.HasMoreReplies = repliesCount > 0;
        }

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
            request.PageSize,
            request.PageNumber);
    }
}
