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
        var commentDtos = await CommentListDtos.Build(comments, mapper, commentsRepository, commentLikesRepository, userContext.GetCurrentUser());
        return new PagedResult<CommentsDTO>(commentDtos, totalCount, pageSize, pageNumber);
    }
}
