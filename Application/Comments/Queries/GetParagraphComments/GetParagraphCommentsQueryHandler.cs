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

        var (pageNumber, pageSize) = Paging.Clamp(request.PageNumber, request.PageSize);
        var (comments, totalCount) = await commentsRepository.GetParagraphComments(
            request.ParagraphId,
            pageNumber,
            pageSize,
            request.Sorting);

        var commentDtos = await CommentListDtos.Build(comments, mapper, commentsRepository, commentLikesRepository, userContext.GetCurrentUser());

        return new PagedResult<CommentsDTO>(
            commentDtos,
            totalCount,
            pageSize,
            pageNumber);
    }
}
