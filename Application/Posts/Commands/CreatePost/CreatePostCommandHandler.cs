using Application.Posts.Queries.GetPost;
using Application.Services;
using Application.Users;
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
    IFileUploadService fileUploadService,
    ISender sender) : IRequestHandler<CreatePostCommand, CreatePostResult>
{
    public async Task<CreatePostResult> Handle(CreatePostCommand request, CancellationToken cancellationToken)
    {
        var currentUser = userContext.GetCurrentUser() ?? throw new ForbidException("سجّل الدخول للمتابعة", "NotSignedIn");
        
        if (request.NovelId.HasValue)
        {
            var novel = await novelsRepository.GetOne(request.NovelId.Value);
            if (novel == null || novel.IsDeleted)
            {
                return new CreatePostResult
                {
                    Success = false,
                    Code = "NovelNotFound",
                    Message = "الرواية غير موجودة"
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

        return new CreatePostResult
        {
            Success = true,
            Message = "نُشر منشورك",
            // Through GET /api/posts/{id} itself, so the app gets the post exactly as the post pages and lists return it.
            Post = await sender.Send(new GetPostQuery(post.Id), cancellationToken)
        };
    }
}    