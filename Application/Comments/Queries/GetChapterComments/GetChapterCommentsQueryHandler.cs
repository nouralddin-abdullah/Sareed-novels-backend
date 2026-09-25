using Application.Comments.DTOS;
using Application.Common;
using Application.Users;
using AutoMapper;
using Domain.Repositories;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Application.Comments.Queries.GetChapterComments;

public class GetChapterCommentsQueryHandler(ILogger<GetChapterCommentsQueryHandler> logger,ICommentsRepository commentsRepository,IUserContext userContext, ICommentLikesRepository commentLikesRepository, IMapper mapper) : IRequestHandler<GetChapterCommentsQuery, PagedResult<CommentsDTO>>
{
    public async Task<PagedResult<CommentsDTO>> Handle(GetChapterCommentsQuery request, CancellationToken cancellationToken)
    {
        logger.LogInformation("Getting comments for chapter {ChapterId}", request.ChapterId);
        var (pageNumber, pageSize) = Paging.Clamp(request.PageNumber, request.PageSize);
        var (comments, totalCount) = await commentsRepository.GetChapterComments(request.ChapterId, pageNumber, pageSize, request.Sorting);
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
        return new PagedResult<CommentsDTO>(commentDtos, totalCount, pageSize, pageNumber);
    }
}
