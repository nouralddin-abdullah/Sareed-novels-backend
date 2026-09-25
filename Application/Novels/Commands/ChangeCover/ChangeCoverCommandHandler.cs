using Application.Covers;
using Application.Services;
using Application.Users;
using Domain.Exceptions;
using Domain.Repositories;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Application.Novels.Commands.ChangeCover
{
    public class ChangeCoverCommandHandler(ILogger<ChangeCoverCommandHandler> logger, INovelCoverService coverService, IUserContext userContext, INovelsRepository novelsRepository) : IRequestHandler<ChangerCoverCommand, ChangeCoverResult>
    {
        public async Task<ChangeCoverResult> Handle(ChangerCoverCommand request, CancellationToken cancellationToken)
        {
            var currentUser = userContext.GetCurrentUser() ?? throw new ForbidException("User not signed in");
            var novel = await novelsRepository.GetOne(request.NovelId) ?? throw new NotFoundException("Novel was not found");
            if (novel.AuthorId != currentUser.Id)
            {
                throw new ForbidException("Forbidden");
            }
            logger.LogInformation("Changing the cover for {NovelId}", novel.Id);

            string coverUrl;
            try
            {
                // A new folder and URL every time: the stored URL must change, or readers keep seeing the old cover.
                await using var stream = request.CoverImageUrl.OpenReadStream();
                coverUrl = await coverService.StoreUploadAsync(novel.Id, stream, cancellationToken);
            }
            catch (CoverImageException ex)
            {
                logger.LogInformation("Refused a new cover for {NovelId}: {Code}", novel.Id, ex.Code);
                return new ChangeCoverResult { Success = false, Message = ex.Message, ErrorCode = ex.Code };
            }

            // One UPDATE of the cover column: saving the whole tracked novel would write back stale view/chapter counters.
            await novelsRepository.SetCoverUrlAsync(novel.Id, coverUrl, cancellationToken: cancellationToken);

            // The previous files stay: notifications and other snapshots may still point at them.
            return new ChangeCoverResult
            {
                Message = "Novel cover was changed successfully",
                Success = true,
                CoverImageUrl = coverUrl
            };
        }
    }
}
