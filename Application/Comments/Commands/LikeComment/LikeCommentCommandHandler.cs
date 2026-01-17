using Application.Users;
using Application.Users.Commands.FollowUser;
using Domain.Entities;
using Domain.Exceptions;
using Domain.Repositories;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Application.Comments.Commands.LikeComment;

public class LikeCommentCommandHandler : IRequestHandler<LikeCommentCommand, OperationResult>
{
    private readonly ILogger<LikeCommentCommandHandler> _logger;
    private readonly IUserContext _userContext;
    private readonly ICommentLikesRepository _commentLikesRepository;
    private readonly ICommentsRepository _commentsRepository;
    private readonly IServiceProvider _serviceProvider;

    public LikeCommentCommandHandler(
        ILogger<LikeCommentCommandHandler> logger,
        IUserContext userContext,
        ICommentLikesRepository commentLikesRepository,
        ICommentsRepository commentsRepository,
        IServiceProvider serviceProvider)
    {
        _logger = logger;
        _userContext = userContext;
        _commentLikesRepository = commentLikesRepository;
        _commentsRepository = commentsRepository;
        _serviceProvider = serviceProvider;
    }

    public async Task<OperationResult> Handle(LikeCommentCommand request, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Liking comment {CommentId}", request.CommentId);

        var currentUser = _userContext.GetCurrentUser()
            ?? throw new ForbidException("User not signed in");

        var comment = await _commentsRepository.GetCommentById(request.CommentId)
            ?? throw new NotFoundException("Comment not found");

        if (comment.UserId == currentUser.Id)
        {
            return new OperationResult
            {
                Success = false,
                Message = "You cannot like your own comment"
            };
        }

        var existingLike = await _commentLikesRepository.GetUserLikeForComment(currentUser.Id, request.CommentId);
        if (existingLike != null)
        {
            return new OperationResult
            {
                Success = false,
                Message = "You already liked this comment"
            };
        }

        var commentLike = new CommentLikes
        {
            UserId = currentUser.Id,
            CommentId = request.CommentId,
            CreatedAt = DateTime.UtcNow
        };

        var result = await _commentLikesRepository.LikeComment(commentLike);
        if (!result)
        {
            return new OperationResult
            {
                Success = false,
                Message = "Failed to like this comment"
            };
        }

        _ = UpdateCommentLikesCountInBackground(request.CommentId, increment: true);
        
        // Fire-and-forget: Send notification
        _ = SendLikeOnCommentNotificationInBackground(comment.UserId, currentUser.Id, request.CommentId);

        _logger.LogInformation("Comment {CommentId} liked successfully by user {UserId}",
            request.CommentId, currentUser.Id);

        return new OperationResult
        {
            Success = true,
            Message = "Comment liked successfully"
        };
    }
    
    private async Task SendLikeOnCommentNotificationInBackground(string commentAuthorId, string likerUserId, Guid commentId)
    {
        try
        {
            using var scope = _serviceProvider.CreateScope();
            var backgroundUserManager = scope.ServiceProvider.GetRequiredService<Microsoft.AspNetCore.Identity.UserManager<User>>();
            var backgroundNotificationService = scope.ServiceProvider.GetRequiredService<Application.Services.INotificationService>();
            var backgroundCommentsRepository = scope.ServiceProvider.GetRequiredService<ICommentsRepository>();
            var backgroundChaptersRepository = scope.ServiceProvider.GetRequiredService<IChaptersRepository>();
            var backgroundParagraphsRepository = scope.ServiceProvider.GetRequiredService<IChapterParagraphsRepository>();
            var backgroundNovelsRepository = scope.ServiceProvider.GetRequiredService<INovelsRepository>();
            var backgroundPostsRepository = scope.ServiceProvider.GetRequiredService<IPostsRepository>();
            
            var liker = await backgroundUserManager.FindByIdAsync(likerUserId);
            var comment = await backgroundCommentsRepository.GetCommentById(commentId);
            
            if (liker != null && comment != null)
            {
                // Get context for the notification URL
                Novel? novel = null;
                Chapter? chapter = null;
                string? postAuthorUsername = null;

                if (comment.ChapterId.HasValue)
                {
                    chapter = await backgroundChaptersRepository.GetChapterById(comment.ChapterId.Value);
                    if (chapter != null)
                    {
                        novel = await backgroundNovelsRepository.GetOne(chapter.NovelId);
                    }
                }
                else if (comment.ParagraphId.HasValue)
                {
                    var paragraph = await backgroundParagraphsRepository.GetParagraphById(comment.ParagraphId.Value);
                    if (paragraph != null)
                    {
                        chapter = await backgroundChaptersRepository.GetChapterById(paragraph.ChapterId);
                        if (chapter != null)
                        {
                            novel = await backgroundNovelsRepository.GetOne(chapter.NovelId);
                        }
                    }
                }
                else if (comment.PostId.HasValue)
                {
                    var post = await backgroundPostsRepository.GetPostById(comment.PostId.Value);
                    if (post != null)
                    {
                        var postAuthor = await backgroundUserManager.FindByIdAsync(post.UserId);
                        postAuthorUsername = postAuthor?.UserName;
                    }
                }

                await backgroundNotificationService.SendLikeOnCommentNotification(
                    commentAuthorId, 
                    liker, 
                    commentId, 
                    comment, 
                    novel, 
                    chapter, 
                    postAuthorUsername);
                    
                _logger.LogDebug("Sent LikeOnComment notification to user {UserId}", commentAuthorId);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to send LikeOnComment notification");
        }
    }

    private async Task UpdateCommentLikesCountInBackground(Guid commentId, bool increment)
    {
        try
        {
            using var scope = _serviceProvider.CreateScope();
            var backgroundCommentLikesRepository = scope.ServiceProvider.GetRequiredService<ICommentLikesRepository>();

            if (increment)
                await backgroundCommentLikesRepository.IncrementCommentLikesCount(commentId);
            else
                await backgroundCommentLikesRepository.DecrementCommentLikesCount(commentId);

            _logger.LogDebug("Successfully updated likes count for comment {CommentId}: {Action}",
                commentId, increment ? "increment" : "decrement");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to update likes count for comment {CommentId} in background", commentId);
        }
    }
}