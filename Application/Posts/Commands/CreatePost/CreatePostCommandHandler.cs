using Application.Posts.Queries.GetPost;
using Application.Services;
using Application.Users;
using Domain.Entities;
using Domain.Exceptions;
using Domain.Repositories;
using FluentValidation;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Application.Posts.Commands.CreatePost;

/// <summary>
/// Checks the post's rules (<see cref="CreatePostCommandValidator"/>), then its novel, then stores its picture, then
/// saves it. A refusal is a result with its code, which the controller answers with 400; a picture that can't be stored
/// is UploadFailed and leaves no post.
/// </summary>
public class CreatePostCommandHandler(
    ILogger<CreatePostCommandHandler> logger,
    IValidator<CreatePostCommand> validator,
    IPostsRepository postsRepository,
    INovelsRepository novelsRepository,
    IUserContext userContext,
    IFileUploadService fileUploadService,
    ISender sender) : IRequestHandler<CreatePostCommand, CreatePostResult>
{
    public async Task<CreatePostResult> Handle(CreatePostCommand request, CancellationToken cancellationToken)
    {
        var currentUser = userContext.GetCurrentUser() ?? throw new ForbidException("سجّل الدخول للمتابعة", "NotSignedIn");

        // The first rule broken is the answer, with its own code (#43).
        var broken = (await validator.ValidateAsync(request, cancellationToken)).Errors.FirstOrDefault();
        if (broken is not null)
        {
            return Refused(broken.ErrorCode, broken.ErrorMessage);
        }

        if (request.NovelId.HasValue)
        {
            var novel = await novelsRepository.GetOne(request.NovelId.Value);
            if (novel == null || novel.IsDeleted)
            {
                return Refused("NovelNotFound", "الرواية غير موجودة");
            }
        }

        var post = new Post
        {
            Id = Guid.NewGuid(),
            UserId = currentUser.Id,
            // Trimmed; the column can't be null, so a post without text has "".
            Content = PostRules.Normalize(request.Content) ?? string.Empty,
            NovelId = request.NovelId,
            CreatedAt = DateTime.UtcNow,
            LikesCount = 0,
            CommentsCount = 0,
            IsDeleted = false
        };

        // The picture is stored before the post is saved, so no post goes out without it.
        if (request.Image != null)
        {
            try
            {
                using var stream = request.Image.OpenReadStream();
                post.ImageUrl = await fileUploadService.UploadPostImageAsync(
                    stream,
                    request.Image.ContentType,
                    post.Id.ToString()
                );
            }
            // A cancelled request isn't a failed upload; a timeout inside the storage client is one.
            catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
            {
                logger.LogError(ex, "Could not store the picture of a new post by user {UserId}; no post was created", currentUser.Id);
                return Refused(PostRules.UploadFailedCode, PostRules.UploadFailedMessage);
            }
        }

        try
        {
            await postsRepository.CreatePost(post);
        }
        catch when (post.ImageUrl is not null)
        {
            // No post will ever show this picture: remove it as far as the storage can, and fail as before (500, logged).
            await DeleteUnusedImage(post.Id, post.ImageUrl);
            throw;
        }

        logger.LogInformation("Post {PostId} created successfully by user {UserId}", post.Id, currentUser.Id);

        return new CreatePostResult
        {
            Success = true,
            Message = "نُشر منشورك",
            // Through GET /api/posts/{id} itself, so the app gets the post exactly as the post pages and lists return it.
            Post = await sender.Send(new GetPostQuery(post.Id), cancellationToken)
        };
    }

    private static CreatePostResult Refused(string code, string message) =>
        new() { Success = false, Code = code, Message = message };

    /// <summary>
    /// Best effort: a picture left behind only costs storage, so failing to delete it is a warning, never thrown over the
    /// save's own error (which the error middleware logs).
    /// </summary>
    private async Task DeleteUnusedImage(Guid postId, string imageUrl)
    {
        try
        {
            if (await fileUploadService.DeleteImageAsync(imageUrl))
            {
                logger.LogInformation("Post {PostId} could not be saved; its picture {ImageUrl} was deleted", postId, imageUrl);
                return;
            }
            logger.LogWarning("Post {PostId} could not be saved, and its picture {ImageUrl} could not be deleted", postId, imageUrl);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Post {PostId} could not be saved, and its picture {ImageUrl} could not be deleted", postId, imageUrl);
        }
    }
}
