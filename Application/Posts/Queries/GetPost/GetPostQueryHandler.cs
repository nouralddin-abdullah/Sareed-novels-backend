using Application.Posts.DTOs;
using Application.Users;
using AutoMapper;
using Domain.Exceptions;
using Domain.Repositories;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Application.Posts.Queries.GetPost;

public class GetPostQueryHandler(
    ILogger<GetPostQueryHandler> logger,
    IPostsRepository postsRepository,
    IPostLikesRepository postLikesRepository,
    IUserBlocksRepository blocksRepository,
    IUserContext userContext,
    IMapper mapper) : IRequestHandler<GetPostQuery, PostDTO>
{
    public async Task<PostDTO> Handle(GetPostQuery request, CancellationToken cancellationToken)
    {
        var post = await postsRepository.GetPostById(request.PostId) ?? throw new NotFoundException("هذا المنشور لم يعد موجودًا", "PostNotFound");
        
        var postDto = mapper.Map<PostDTO>(post);
        
        var currentUser = userContext.GetCurrentUser();
        if (currentUser != null)
        {
            // To someone its author blocked (both blocking each other included), the post is unavailable; someone who
            // blocked its author still sees it, flagged, so they can unblock (PostBlocks). One query for both.
            if (currentUser.Id != post.UserId)
            {
                var relation = await blocksRepository.GetRelationAsync(currentUser.Id, post.UserId, cancellationToken);
                if (relation.OtherBlockedViewer)
                {
                    throw PostBlocks.Unavailable();
                }
                postDto.AuthorBlockedByMe = relation.ViewerBlockedOther;
            }

            postDto.IsLikedByCurrentUser = await postLikesRepository.HasUserLikedPost(currentUser.Id, post.Id);
        }
        
        logger.LogInformation("Retrieved post {PostId}", request.PostId);
        
        return postDto;
    }
}
