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
    IUserContext userContext,
    IMapper mapper) : IRequestHandler<GetUserPostsQuery, PagedResult<PostDTO>>
{
    public async Task<PagedResult<PostDTO>> Handle(GetUserPostsQuery request, CancellationToken cancellationToken)
    {
        var (posts, totalCount) = await postsRepository.GetUserPosts(request.UserId, request.PageNumber, request.PageSize);
        
        var postDtos = mapper.Map<IEnumerable<PostDTO>>(posts).ToList();
        
        var currentUser = userContext.GetCurrentUser();
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
        
        return new PagedResult<PostDTO>(postDtos, totalCount, request.PageSize, request.PageNumber);
    }
}
