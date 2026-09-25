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

        _logger.LogInformation("Comment {CommentId} unliked successfully by user {UserId}",
            request.CommentId, currentUser.Id);

        return new OperationResult
        {
            Success = true,
            Message = "Comment unliked successfully"
        };
    }
}