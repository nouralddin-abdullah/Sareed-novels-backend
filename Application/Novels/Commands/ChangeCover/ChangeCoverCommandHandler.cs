using Application.Services;
using Application.Users;
using Application.Users.Commands.FollowUser;
using Domain.Exceptions;
using Domain.Repositories;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Application.Novels.Commands.ChangeCover
{
    public class ChangeCoverCommandHandler(ILogger<ChangeCoverCommandHandler> logger, IFileUploadService fileUploadService, IUserContext userContext, INovelsRepository novelsRepository) : IRequestHandler<ChangerCoverCommand, OperationResult>
    {
        public async Task<OperationResult> Handle(ChangerCoverCommand request, CancellationToken cancellationToken)
        {
            var currentUser = userContext.GetCurrentUser() ?? throw new ForbidException("User not signed in");
            var novel = await novelsRepository.GetOne(request.NovelId) ?? throw new NotFoundException("Novel was not found");
            if (novel.AuthorId != currentUser.Id)
            {
                throw new ForbidException("Forbidden");
            }
            logger.LogInformation("Changing the cover for {@novel}", novel);

            try
            {
                if (request.CoverImageUrl != null)
                {
                    using var stream = request.CoverImageUrl.OpenReadStream();
                    await fileUploadService.UploadNovelImageAsync(
                        stream,
                        request.CoverImageUrl.ContentType,
                        novel.Title
                        );
                }
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to change cover for novel {NovelId}", novel.Id);
                throw new InvalidOperationException("Failed to change novel cover", ex);
            }

            return new OperationResult
            {
                Message = "Novel cover was changed successfully",
                Success = true

            };
        }
    }
}
