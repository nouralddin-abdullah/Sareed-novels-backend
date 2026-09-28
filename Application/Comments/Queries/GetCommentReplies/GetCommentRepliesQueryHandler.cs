using Application.Comments.DTOS;
using Application.Common;
using Application.Users;
using AutoMapper;
using Domain.Exceptions;
using Domain.Repositories;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Application.Comments.Queries.GetCommentReplies;

public class GetCommentRepliesQueryHandler(ILogger<GetCommentRepliesQueryHandler> logger, ICommentsRepository commentsRepository, ICommentLikesRepository commentLikesRepository, IUserContext userContext, IMapper mapper) : IRequestHandler<GetCommentRepliesQuery, PagedResult<CommentReplyDTO>>
{
    public async Task<PagedResult<CommentReplyDTO>> Handle(GetCommentRepliesQuery request, CancellationToken cancellationToken)
    {
        logger.LogInformation("Getting replies for comment {ParentCommentId}", request.ParentCommentId);
        var parentComment = await commentsRepository.GetCommentById(request.ParentCommentId) ?? throw new NotFoundException("هذا التعليق لم يعد موجودًا", "ParentCommentNotFound");
        var (pageNumber, pageSize) = Paging.Clamp(request.PageNumber, request.PageSize);
        // Replies by users the viewer blocked are left out.
        var currentUser = userContext.GetCurrentUser();
        var (replies, totalCount) = await commentsRepository.GetCommentReplies(request.ParentCommentId, pageNumber, pageSize, request.Sorting, currentUser?.Id);
        var replyDtos = mapper.Map<List<CommentReplyDTO>>(replies);
        if (currentUser != null && replyDtos.Any())
        {
            var replyIds = replyDtos.Select(r => r.Id);
            var likedReplyIds = await commentLikesRepository.GetUserLikedCommentIds(currentUser.Id, replyIds);

            // Mark which replies are liked by current user
            foreach (var replyDto in replyDtos)
            {
                replyDto.IsLikedByCurrentUser = likedReplyIds.Contains(replyDto.Id);
            }
        }
        return new PagedResult<CommentReplyDTO>(
            replyDtos,
            totalCount,
            pageSize,
            pageNumber);

    }
}
