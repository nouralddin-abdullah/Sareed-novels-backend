using Application.Services;
using Application.Users;
using Application.Users.Commands.FollowUser;
using Domain.Entities;
using Domain.Exceptions;
using Domain.Repositories;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Application.Posts.Commands.CreatePost;

public class CreatePostCommandHandler(
    ILogger<CreatePostCommandHandler> logger,
    IPostsRepository postsRepository,
    INovelsRepository novelsRepository,
    IUserContext userContext,
    IFileUploadService fileUploadService) : IRequestHandler<CreatePostCommand, OperationResult>
{
    public async Task<OperationResult> Handle(CreatePostCommand request, CancellationToken cancellationToken)
    {
        var currentUser = userContext.GetCurrentUser() ?? throw new ForbidException("User not signed in");
        
        if (request.NovelId.HasValue)
        {
            var novel = await novelsRepository.GetOne(request.NovelId.Value);
            if (novel == null || novel.IsDeleted)
            {
                return new OperationResult
                {
                    Success = false,
                    Message = "Novel not found"
                };
            }
        }
        
        var post = new Post
        {
            Id = Guid.NewGuid(),
            UserId = currentUser.Id,
            Content = request.Content,
            NovelId = request.NovelId,
            CreatedAt = DateTime.UtcNow,
            LikesCount = 0,
            CommentsCount = 0,
            IsDeleted = false
        };
        
        if (request.Image != null)
        {
            using var stream = request.Image.OpenReadStream();
            post.ImageUrl = await fileUploadService.UploadPostImageAsync(
                stream,
                request.Image.ContentType,
                post.Id.ToString()
            );
        }
        
        await postsRepository.CreatePost(post);
        
        logger.LogInformation("Post {PostId} created successfully by user {UserId}", post.Id, currentUser.Id);

        return new OperationResult
        {
            Success = true,
            Message = "Post created successfully"
        };
    }
}    