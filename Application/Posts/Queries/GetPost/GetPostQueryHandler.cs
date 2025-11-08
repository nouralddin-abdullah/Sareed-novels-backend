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
    IUserContext userContext,
    IMapper mapper) : IRequestHandler<GetPostQuery, PostDTO>
{
    public async Task<PostDTO> Handle(GetPostQuery request, CancellationToken cancellationToken)
    {
        var post = await postsRepository.GetPostById(request.PostId) ?? throw new NotFoundException("Post not found");
        
        var postDto = mapper.Map<PostDTO>(post);
        
        var currentUser = userContext.GetCurrentUser();
        if (currentUser != null)
        {
            postDto.IsLikedByCurrentUser = await postLikesRepository.HasUserLikedPost(currentUser.Id, post.Id);
        }
        
        logger.LogInformation("Retrieved post {PostId}", request.PostId);
        
        return postDto;
    }
}
