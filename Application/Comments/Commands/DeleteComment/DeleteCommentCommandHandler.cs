using Application.Comments.Commands.CreateComment;
using Application.Services;
using Application.Users;
using Domain.Entities;
using Domain.Exceptions;
using Domain.Repositories;
using MediatR;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Application.Comments.Commands.DeleteComment;

internal class DeleteCommentCommandHandler(
    ILogger<CreateCommentCommandHandler> logger, 
    ICommentsRepository commentsRepository, 
    IUserContext userContext, 
    IServiceProvider serviceProvider) : IRequestHandler<DeleteCommentCommand, bool>
{
    public async Task<bool> Handle(DeleteCommentCommand request, CancellationToken cancellationToken)
    {
        var currentUser = userContext.GetCurrentUser() ?? throw new ForbidException("User not found!");
        var comment = await commentsRepository.GetCommentById(request.CommentId) ?? throw new NotFoundException("This comment wasn't found!");
        if (currentUser.Id != comment.UserId)
            throw new ForbidException("Not comment owner!");
        
        // Fire-and-forget: Decrement user's comments count
        _ = DecrementUserCommentsCountInBackground(currentUser.Id);
        
        // Only decrement counts for top-level comments (not replies)
        if (!comment.ParentCommentId.HasValue)
        {
            if (comment.ParagraphId.HasValue)
            {
                // Paragraph comment - need to get chapterId from paragraph
                _ = UpdateParagraphCommentsCountInBackground(comment.ParagraphId.Value);
            }
            else if (comment.ChapterId.HasValue)
            {
                // Chapter-level comment - decrement chapter count only
                _ = UpdateChapterCommentsCountInBackground(comment.ChapterId.Value);
            }
        }
        
        return await commentsRepository.DeleteComment(comment.Id);
    }
    
    private async Task DecrementUserCommentsCountInBackground(string userId)
    {
        try
        {
            using var scope = serviceProvider.CreateScope();
            var backgroundUserManager = scope.ServiceProvider.GetRequiredService<UserManager<User>>();
            
            var user = await backgroundUserManager.FindByIdAsync(userId);
            if (user != null)
            {
                user.DecrementCommentsCount();
                await backgroundUserManager.UpdateAsync(user);
            }
            logger.LogDebug("Successfully decremented comments count for user {UserId}", userId);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to decrement comments count for user {UserId}", userId);
        }
    }

    private async Task UpdateChapterCommentsCountInBackground(Guid chapterId)
    {
        try
        {
            // Create a new scope for background operation
            using var scope = serviceProvider.CreateScope();
            var backgroundChaptersRepository = scope.ServiceProvider.GetRequiredService<IChaptersRepository>();

            var chapter = await backgroundChaptersRepository.GetChapterById(chapterId);
            if (chapter != null)
            {
                chapter.DecrementCommentsCount();
                await backgroundChaptersRepository.UpdateChapter(chapter);
            }

            logger.LogDebug("Successfully updated comments count for chapter {ChapterId}", chapterId);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to update comments count for chapter {ChapterId} in background", chapterId);
        }
    }
    
    private async Task UpdateParagraphCommentsCountInBackground(Guid paragraphId)
    {
        try
        {
            using var scope = serviceProvider.CreateScope();
            var backgroundParagraphsRepository = scope.ServiceProvider.GetRequiredService<IChapterParagraphsRepository>();
            var backgroundChaptersRepository = scope.ServiceProvider.GetRequiredService<IChaptersRepository>();

            // Get the paragraph to find its chapterId
            var paragraph = await backgroundParagraphsRepository.GetParagraphById(paragraphId);
            if (paragraph != null)
            {
                // Decrement paragraph count
                paragraph.DecrementCommentsCount();
                await backgroundParagraphsRepository.UpdateParagraph(paragraph);
                
                // Decrement chapter total count
                var chapter = await backgroundChaptersRepository.GetChapterById(paragraph.ChapterId);
                if (chapter != null)
                {
                    chapter.DecrementTotalCommentsCount();
                    await backgroundChaptersRepository.UpdateChapter(chapter);
                }
                
                logger.LogDebug("Successfully updated comments count for paragraph {ParagraphId} and chapter {ChapterId}", paragraphId, paragraph.ChapterId);
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to update comments count for paragraph {ParagraphId} in background", paragraphId);
        }
    }
}
