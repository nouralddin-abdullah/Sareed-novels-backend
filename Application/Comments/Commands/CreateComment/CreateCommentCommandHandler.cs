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

public class CreateCommentCommandHandler(ILogger<CreateCommentCommandHandler> logger, ICommentsRepository commentsRepository, IChaptersRepository chaptersRepository, IUserContext userContext, IFileUploadService fileUploadService, IServiceProvider serviceProvider) : IRequestHandler<CreateCommentCommand, OperationResult>
{
    public async Task<OperationResult> Handle(CreateCommentCommand request, CancellationToken cancellationToken)
    {
        var currentUser = userContext.GetCurrentUser() ?? throw new ForbidException("User not signed in");
        logger.LogInformation("Creating comment for chapter {ChapterId}", request.ChapterId);
        var chapter = await chaptersRepository.GetChapterById(request.ChapterId) ?? throw new NotFoundException("Chapter not found");
        if (request.ParentCommentId != null)
        {
            var parentComment = await commentsRepository.GetCommentById(request.ParentCommentId.Value) ?? throw new NotFoundException("Parent comment not found!");
        }
        var comment = new Domain.Entities.Comments
        {
            Id = Guid.NewGuid(),
            ChapterId = chapter.Id,
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
        if (!request.ParentCommentId.HasValue)
        {
            _ = UpdateChapterCommentsCountInBackground(request.ChapterId);
        }
        logger.LogInformation("Comment {CommentId} created successfully for chapter {ChapterId}",
            createdComment.Id, request.ChapterId);

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
            // Create a new scope for background operation
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
}
