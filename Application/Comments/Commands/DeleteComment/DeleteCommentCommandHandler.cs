using Application.Users;
using Domain.Exceptions;
using Domain.Repositories;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Application.Comments.Commands.DeleteComment;

internal class DeleteCommentCommandHandler(
    ILogger<DeleteCommentCommandHandler> logger, 
    ICommentsRepository commentsRepository, 
    IUserContext userContext) : IRequestHandler<DeleteCommentCommand, bool>
{
    public async Task<bool> Handle(DeleteCommentCommand request, CancellationToken cancellationToken)
    {
        var currentUser = userContext.GetCurrentUser() ?? throw new ForbidException("User not found!");
        var comment = await commentsRepository.GetCommentById(request.CommentId) ?? throw new NotFoundException("This comment wasn't found!");
        if (currentUser.Id != comment.UserId)
            throw new ForbidException("Not comment owner!");

        // Soft-deletes the comment and lowers the user's and the post/chapter/paragraph's counters in one transaction.
        var deleted = await commentsRepository.DeleteComment(comment.Id);
        if (deleted)
        {
            logger.LogInformation("Comment {CommentId} deleted by user {UserId}", comment.Id, currentUser.Id);
        }
        return deleted;
    }
}
