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
    IPostsRepository postsRepository,
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
            // The query filter hides deleted posts, so they are "not found" too.
            var post = await postsRepository.GetPostById(request.PostId.Value) ?? throw new NotFoundException("Post not found");
            postId = post.Id;
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

            // Threads are one level deep (the web app only shows replies under top-level comments), and a reply lives
            // where its parent does.
            if (parentComment.ParentCommentId.HasValue)
            {
                return new OperationResult { Success = false, Message = "Replies can only be added to top-level comments" };
            }

            var sameLocation = request.PostId.HasValue ? parentComment.PostId == request.PostId
                : request.ParagraphId.HasValue ? parentComment.ParagraphId == request.ParagraphId
                : parentComment.ChapterId == request.ChapterId && parentComment.ParagraphId == null;
            if (!sameLocation)
            {
                return new OperationResult { Success = false, Message = "The parent comment belongs to a different chapter, paragraph or post" };
            }
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
        
        // Saves the comment and bumps the user's and the post/chapter/paragraph's counters in one transaction.
        var createdComment = await commentsRepository.CreateComment(comment);
        
        // Fire-and-forget: Send notifications
        _ = SendCommentNotificationsInBackground(createdComment.Id, currentUser.Id, chapterId, postId, request.ParentCommentId);
        
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
}
