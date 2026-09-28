using Application.Users;
using Application.Users.Commands.FollowUser;
using Domain.Exceptions;
using Domain.Repositories;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Application.Posts.Commands.UnlikePost;

public class UnlikePostCommandHandler(
    ILogger<UnlikePostCommandHandler> logger,
    IPostsRepository postsRepository,
    IPostLikesRepository postLikesRepository,
    IUserContext userContext) : IRequestHandler<UnlikePostCommand, OperationResult>
{
    public async Task<OperationResult> Handle(UnlikePostCommand request, CancellationToken cancellationToken)
    {
        var currentUser = userContext.GetCurrentUser() ?? throw new ForbidException("سجّل الدخول للمتابعة", "NotSignedIn");
        
        var post = await postsRepository.GetPostById(request.PostId);
        if (post == null || post.IsDeleted)
        {
            return new OperationResult
            {
                Success = false,
                Code = "PostNotFound",
                Message = "هذا المنشور لم يعد موجودًا"
            };
        }

        if (!await postLikesRepository.UnLikePost(currentUser.Id, request.PostId))
        {
            return new OperationResult
            {
                Success = false,
                Code = "NotLiked",
                Message = "لم تُبدِ إعجابك بهذا المنشور"
            };
        }

        logger.LogInformation("User {UserId} unliked post {PostId}", currentUser.Id, request.PostId);

        return new OperationResult
        {
            Success = true,
            Message = "أُلغي إعجابك بالمنشور"
        };
    }
}
