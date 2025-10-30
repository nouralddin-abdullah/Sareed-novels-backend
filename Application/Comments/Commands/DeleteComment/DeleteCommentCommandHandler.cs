using Application.Comments.Commands.CreateComment;
using Application.Services;
using Application.Users;
using Domain.Exceptions;
using Domain.Repositories;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Application.Comments.Commands.DeleteComment;

internal class DeleteCommentCommandHandler(ILogger<CreateCommentCommandHandler> logger, ICommentsRepository commentsRepository, IUserContext userContext, IServiceProvider serviceProvider) : IRequestHandler<DeleteCommentCommand, bool>
{
    public async Task<bool> Handle(DeleteCommentCommand request, CancellationToken cancellationToken)
    {
        var currentUser = userContext.GetCurrentUser() ?? throw new ForbidException("User not found!");
        var comment = await commentsRepository.GetCommentById(request.CommentId) ?? throw new NotFoundException("This comment wasn't found!");
        if (currentUser.Id != comment.UserId)
            throw new ForbidException("Not comment owner!");
        if (!comment.ParentCommentId.HasValue && comment.ChapterId.HasValue)
        {
            _ = UpdateChapterCommentsCountInBackground(comment.ChapterId.Value);
        }
        return await commentsRepository.DeleteComment(comment.Id);
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
}
