using Application.Common;
using Application.Posts.DTOs;
using Application.Users;
using AutoMapper;
using Domain.Repositories;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Application.Posts.Queries.GetUserPosts;

public class GetUserPostsQueryHandler(
    ILogger<GetUserPostsQueryHandler> logger,
    IPostsRepository postsRepository,
    IPostLikesRepository postLikesRepository,
    IUserBlocksRepository blocksRepository,
    IUserContext userContext,
    IMapper mapper) : IRequestHandler<GetUserPostsQuery, PagedResult<PostDTO>>
{
    public async Task<PagedResult<PostDTO>> Handle(GetUserPostsQuery request, CancellationToken cancellationToken)
    {
        var (pageNumber, pageSize) = Paging.Clamp(request.PageNumber, request.PageSize);
        var currentUser = userContext.GetCurrentUser();

        // Posts of a user the viewer blocked are left out, and so, for someone they blocked, are the posts of a user
        // whose profile they can't open: an empty page, as for a user who doesn't exist.
        if (currentUser != null && currentUser.Id != request.UserId
            && (await blocksRepository.GetRelationAsync(currentUser.Id, request.UserId, cancellationToken)).Either)
        {
            return new PagedResult<PostDTO>([], 0, pageSize, pageNumber);
        }

        var (posts, totalCount) = await postsRepository.GetUserPosts(request.UserId, pageNumber, pageSize);
        
        var postDtos = mapper.Map<IEnumerable<PostDTO>>(posts).ToList();
        
        if (currentUser != null && postDtos.Any())
        {
            var postIds = postDtos.Select(p => p.Id).ToList();
            var likedPostIds = await postLikesRepository.GetUserLikedPostIds(currentUser.Id, postIds);
            
            foreach (var postDto in postDtos)
            {
                postDto.IsLikedByCurrentUser = likedPostIds.Contains(postDto.Id);
            }
        }
        
        logger.LogInformation("Retrieved {Count} posts for user {UserId}", postDtos.Count, request.UserId);
        
        return new PagedResult<PostDTO>(postDtos, totalCount, pageSize, pageNumber);
    }
}
