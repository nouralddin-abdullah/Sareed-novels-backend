using Application.Users;
using Application.Users.Commands.FollowUser;
using Domain.Exceptions;
using Domain.Repositories;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Application.Comments.Commands.UnlikeComment;

public class UnlikeCommentCommandHandler : IRequestHandler<UnlikeCommentCommand, OperationResult>
{
    private readonly ILogger<UnlikeCommentCommandHandler> _logger;
    private readonly IUserContext _userContext;
    private readonly ICommentLikesRepository _commentLikesRepository;
    private readonly IServiceProvider _serviceProvider;

    public UnlikeCommentCommandHandler(
        ILogger<UnlikeCommentCommandHandler> logger,
        IUserContext userContext,
        ICommentLikesRepository commentLikesRepository,
        IServiceProvider serviceProvider)
    {
        _logger = logger;
        _userContext = userContext;
        _commentLikesRepository = commentLikesRepository;
        _serviceProvider = serviceProvider;
    }

    public async Task<OperationResult> Handle(UnlikeCommentCommand request, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Unliking comment {CommentId}", request.CommentId);

        var currentUser = _userContext.GetCurrentUser()
            ?? throw new ForbidException("User not signed in");

        var result = await _commentLikesRepository.UnLikeComment(currentUser.Id, request.CommentId);
        if (!result)
        {
            return new OperationResult
            {
                Success = false,
                Message = "You haven't liked this comment or it doesn't exist"
            };
        }

        // Fire-and-forget: Update likes count in background
        _ = UpdateCommentLikesCountInBackground(request.CommentId, increment: false);

        _logger.LogInformation("Comment {CommentId} unliked successfully by user {UserId}",
            request.CommentId, currentUser.Id);

        return new OperationResult
        {
            Success = true,
            Message = "Comment unliked successfully"
        };
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