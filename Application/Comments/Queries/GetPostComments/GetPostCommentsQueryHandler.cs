using Application.Comments.DTOS;
using Application.Common;
using Application.Posts;
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
    IPostsRepository postsRepository,
    IUserBlocksRepository blocksRepository,
    IUserContext userContext,
    IMapper mapper) : IRequestHandler<GetPostCommentsQuery, PagedResult<CommentsDTO>>
{
    public async Task<PagedResult<CommentsDTO>> Handle(GetPostCommentsQuery request, CancellationToken cancellationToken)
    {
        logger.LogInformation("Getting comments for post {PostId}, page {PageNumber}, sorting {Sorting}",
            request.PostId, request.PageNumber, request.Sorting);

        var (pageNumber, pageSize) = Paging.Clamp(request.PageNumber, request.PageSize);
        var currentUser = userContext.GetCurrentUser();
        // To someone the post's author blocked, the post is unavailable, and so are its comments (PostBlocks).
        await PostBlocks.EnsureNotBlockedByAuthorAsync(postsRepository, blocksRepository, request.PostId,
            currentUser?.Id, cancellationToken);

        // Comments by users the viewer blocked are left out.
        var (comments, totalCount) = await commentsRepository.GetPostComments(
            request.PostId,
            pageNumber,
            pageSize,
            request.Sorting,
            currentUser?.Id);

        var commentDtos = await CommentListDtos.Build(comments, mapper, commentsRepository, commentLikesRepository, currentUser);

        return new PagedResult<CommentsDTO>(
            commentDtos,
            totalCount,
            pageSize,
            pageNumber);
    }
}
