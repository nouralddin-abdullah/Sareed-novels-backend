using Application.Services;
using Application.Users;
using Application.Users.Commands.FollowUser;
using Domain.Entities;
using Domain.Exceptions;
using Domain.Repositories;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Application.Comments.Commands.CreateComment;

public class CreateCommentCommandHandler(ILogger<CreateCommentCommandHandler> logger, ICommentsRepository commentsRepository, IChaptersRepository chaptersRepository, IChapterParagraphsRepository paragraphsRepository, IUserContext userContext, IFileUploadService fileUploadService, IServiceProvider serviceProvider) : IRequestHandler<CreateCommentCommand, OperationResult>
{
    public async Task<OperationResult> Handle(CreateCommentCommand request, CancellationToken cancellationToken)
    {
        var currentUser = userContext.GetCurrentUser() ?? throw new ForbidException("User not signed in");
        
        Guid chapterId;
        
        // Validate either chapter or paragraph is provided
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
        else
        {
            return new OperationResult
            {
                Success = false,
                Message = "Either ChapterId or ParagraphId must be provided"
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
        
        // Fire-and-forget counter updates (only for top-level comments)
        if (!request.ParentCommentId.HasValue)
        {
            if (request.ParagraphId.HasValue)
            {
                _ = UpdateParagraphCommentsCountInBackground(request.ParagraphId.Value, chapterId);
            }
            else if (request.ChapterId.HasValue)
            {
                _ = UpdateChapterCommentsCountInBackground(request.ChapterId.Value);
            }
        }
        
        logger.LogInformation("Comment {CommentId} created successfully", createdComment.Id);

        return new OperationResult
        {
            Success = true,
            Message = "Comment created successfully"
        };
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
}
