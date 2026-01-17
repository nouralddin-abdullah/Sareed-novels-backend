using Application.Services;
using Application.Users;
using Application.Users.Commands.FollowUser;
using Domain.Entities;
using Domain.Exceptions;
using Domain.Repositories;
using MediatR;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Application.Comments.Commands.CreateComment;

public class CreateCommentCommandHandler(
    ILogger<CreateCommentCommandHandler> logger, 
    ICommentsRepository commentsRepository, 
    IChaptersRepository chaptersRepository, 
    IChapterParagraphsRepository paragraphsRepository, 
    IUserContext userContext, 
    IFileUploadService fileUploadService, 
    IServiceProvider serviceProvider) : IRequestHandler<CreateCommentCommand, OperationResult>
{
    public async Task<OperationResult> Handle(CreateCommentCommand request, CancellationToken cancellationToken)
    {
        var currentUser = userContext.GetCurrentUser() ?? throw new ForbidException("User not signed in");
        
        Guid? chapterId = null;
        Guid? postId = null;
        
        if (request.ParagraphId.HasValue)
        {
            logger.LogInformation("Creating comment for paragraph {ParagraphId}", request.ParagraphId);
            var paragraph = await paragraphsRepository.GetParagraphById(request.ParagraphId.Value) ?? throw new NotFoundException("Paragraph not found");
            chapterId = paragraph.ChapterId;
        }
        else if (request.ChapterId.HasValue)
        {
            logger.LogInformation("Creating comment for chapter {ChapterId}", request.ChapterId);
            var chapter = await chaptersRepository.GetChapterById(request.ChapterId.Value) ?? throw new NotFoundException("Chapter not found");
            chapterId = chapter.Id;
        }
        else if (request.PostId.HasValue)
        {
            logger.LogInformation("Creating comment for post {PostId}", request.PostId);
            postId = request.PostId.Value;
        }
        else
        {
            return new OperationResult
            {
                Success = false,
                Message = "Either ChapterId, ParagraphId, or PostId must be provided"
            };
        }
        
        if (request.ParentCommentId.HasValue)
        {
            var parentComment = await commentsRepository.GetCommentById(request.ParentCommentId.Value) ?? throw new NotFoundException("Parent comment not found!");
        }
        
        var comment = new Domain.Entities.Comments
        {
            Id = Guid.NewGuid(),
            ChapterId = request.ChapterId,
            ParagraphId = request.ParagraphId,
            PostId = request.PostId,
            UserId = currentUser.Id,
            Content = request.Content,
            ParentCommentId = request.ParentCommentId,
            CreatedAt = DateTime.UtcNow,
            LikesCount = 0,
            IsDeleted = false
        };
        
        if (request.AttachedImage != null)
        {
            using var stream = request.AttachedImage.OpenReadStream();
            comment.AttachedImageUrl = await fileUploadService.UploadCommentImageAsync(
                stream,
                request.AttachedImage.ContentType,
                comment.Id.ToString()
            );
        }
        
        var createdComment = await commentsRepository.CreateComment(comment);
        
        // Fire-and-forget: Send notifications
        _ = SendCommentNotificationsInBackground(createdComment.Id, currentUser.Id, chapterId, postId, request.ParentCommentId);
        
        _ = IncrementUserCommentsCountInBackground(currentUser.Id);

        if (!request.ParentCommentId.HasValue)
        {
            if (request.ParagraphId.HasValue)
            {
                _ = UpdateParagraphCommentsCountInBackground(request.ParagraphId.Value, chapterId!.Value);
            }
            else if (request.ChapterId.HasValue)
            {
                _ = UpdateChapterCommentsCountInBackground(request.ChapterId.Value);
            }
            else if (request.PostId.HasValue)
            {
                _ = UpdatePostCommentsCountInBackground(request.PostId.Value);
            }
        }
        
        logger.LogInformation("Comment {CommentId} created successfully", createdComment.Id);

        return new OperationResult
        {
            Success = true,
            Message = "Comment created successfully"
        };
    }
    
    private async Task SendCommentNotificationsInBackground(Guid commentId, string commenterUserId, Guid? chapterId, Guid? postId, Guid? parentCommentId)
    {
        try
        {
            using var scope = serviceProvider.CreateScope();
            var backgroundUserManager = scope.ServiceProvider.GetRequiredService<UserManager<User>>();
            var backgroundNotificationService = scope.ServiceProvider.GetRequiredService<INotificationService>();
            var backgroundChaptersRepository = scope.ServiceProvider.GetRequiredService<IChaptersRepository>();
            var backgroundNovelsRepository = scope.ServiceProvider.GetRequiredService<INovelsRepository>();
            var backgroundPostsRepository = scope.ServiceProvider.GetRequiredService<IPostsRepository>();
            var backgroundCommentsRepository = scope.ServiceProvider.GetRequiredService<ICommentsRepository>();

            var commenter = await backgroundUserManager.FindByIdAsync(commenterUserId);
            if (commenter == null) return;

            if (parentCommentId.HasValue)
            {
                // Reply to comment notification
                var parentComment = await backgroundCommentsRepository.GetCommentById(parentCommentId.Value);
                if (parentComment != null)
                {
                    // Get context for the reply notification URL
                    Novel? novel = null;
                    Chapter? chapter = null;
                    string? postAuthorUsername = null;

                    if (parentComment.ChapterId.HasValue)
                    {
                        chapter = await backgroundChaptersRepository.GetChapterById(parentComment.ChapterId.Value);
                        if (chapter != null)
                        {
                            novel = await backgroundNovelsRepository.GetOne(chapter.NovelId);
                        }
                    }
                    else if (parentComment.ParagraphId.HasValue)
                    {
                        var paragraph = await scope.ServiceProvider.GetRequiredService<IChapterParagraphsRepository>()
                            .GetParagraphById(parentComment.ParagraphId.Value);
                        if (paragraph != null)
                        {
                            chapter = await backgroundChaptersRepository.GetChapterById(paragraph.ChapterId);
                            if (chapter != null)
                            {
                                novel = await backgroundNovelsRepository.GetOne(chapter.NovelId);
                            }
                        }
                    }
                    else if (parentComment.PostId.HasValue)
                    {
                        var post = await backgroundPostsRepository.GetPostById(parentComment.PostId.Value);
                        if (post != null)
                        {
                            var postAuthor = await backgroundUserManager.FindByIdAsync(post.UserId);
                            postAuthorUsername = postAuthor?.UserName;
                        }
                    }

                    await backgroundNotificationService.SendReplyToCommentNotification(
                        parentComment.UserId,
                        commenter,
                        commentId,
                        parentComment,
                        novel,
                        chapter,
                        postAuthorUsername);
                }
            }
            else if (chapterId.HasValue)
            {
                // Comment on chapter notification
                var chapter = await backgroundChaptersRepository.GetChapterById(chapterId.Value);
                if (chapter != null)
                {
                    var novel = await backgroundNovelsRepository.GetOne(chapter.NovelId);
                    if (novel != null)
                    {
                        await backgroundNotificationService.SendCommentOnChapterNotification(
                            novel.AuthorId,
                            commenter,
                            commentId,
                            novel,
                            chapter);
                    }
                }
            }
            else if (postId.HasValue)
            {
                // Comment on post notification - need to include User for username
                var post = await backgroundPostsRepository.GetPostById(postId.Value);
                if (post != null)
                {
                    // Fetch the post author's username
                    var postAuthor = await backgroundUserManager.FindByIdAsync(post.UserId);
                    if (postAuthor != null)
                    {
                        await backgroundNotificationService.SendCommentOnPostNotification(
                            post.UserId,
                            commenter,
                            commentId,
                            postAuthor.UserName!);
                    }
                }
            }

            logger.LogDebug("Successfully sent comment notifications");
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to send comment notifications");
        }
    }

    private async Task IncrementUserCommentsCountInBackground(string userId)
    {
        try
        {
            using var scope = serviceProvider.CreateScope();
            var backgroundUserManager = scope.ServiceProvider.GetRequiredService<UserManager<User>>();
            
            var user = await backgroundUserManager.FindByIdAsync(userId);
            if (user != null)
            {
                user.IncrementCommentsCount();
                await backgroundUserManager.UpdateAsync(user);
            }
            logger.LogDebug("Successfully incremented comments count for user {UserId}", userId);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to increment comments count for user {UserId}", userId);
        }
    }

    private async Task UpdateChapterCommentsCountInBackground(Guid chapterId)
    {
        try
        {
            using var scope = serviceProvider.CreateScope();
            var backgroundChaptersRepository = scope.ServiceProvider.GetRequiredService<IChaptersRepository>();

            var chapter = await backgroundChaptersRepository.GetChapterById(chapterId);
            if (chapter != null)
            {
                chapter.IncrementCommentsCount();
                await backgroundChaptersRepository.UpdateChapter(chapter);
            }

            logger.LogDebug("Successfully updated comments count for chapter {ChapterId}", chapterId);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to update comments count for chapter {ChapterId} in background", chapterId);
        }
    }
    
    private async Task UpdateParagraphCommentsCountInBackground(Guid paragraphId, Guid chapterId)
    {
        try
        {
            using var scope = serviceProvider.CreateScope();
            var backgroundParagraphsRepository = scope.ServiceProvider.GetRequiredService<IChapterParagraphsRepository>();
            var backgroundChaptersRepository = scope.ServiceProvider.GetRequiredService<IChaptersRepository>();

            var paragraph = await backgroundParagraphsRepository.GetParagraphById(paragraphId);
            if (paragraph != null)
            {
                paragraph.IncrementCommentsCount();
                await backgroundParagraphsRepository.UpdateParagraph(paragraph);
            }
            
            var chapter = await backgroundChaptersRepository.GetChapterById(chapterId);
            if (chapter != null)
            {
                chapter.IncrementTotalCommentsCount();
                await backgroundChaptersRepository.UpdateChapter(chapter);
            }

            logger.LogDebug("Successfully updated comments count for paragraph {ParagraphId}", paragraphId);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to update comments count for paragraph {ParagraphId} in background", paragraphId);
        }
    }

    private async Task UpdatePostCommentsCountInBackground(Guid postId)
    {
        try
        {
            using var scope = serviceProvider.CreateScope();
            var backgroundPostsRepository = scope.ServiceProvider.GetRequiredService<IPostsRepository>();

            var post = await backgroundPostsRepository.GetPostById(postId);
            if (post != null)
            {
                post.IncrementCommentsCount();
                await backgroundPostsRepository.UpdatePost(post);
            }

            logger.LogDebug("Successfully updated comments count for post {PostId}", postId);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to update comments count for post {PostId} in background", postId);
        }
    }
}
