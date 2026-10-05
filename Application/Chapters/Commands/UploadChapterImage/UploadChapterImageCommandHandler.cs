using Application.Covers;
using Application.Services;
using Application.Users;
using Domain.Exceptions;
using Domain.Repositories;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Application.Chapters.Commands.UploadChapterImage;

/// <summary>
/// Stores a chapter picture for the novel's author (#86), drafts included. Refusals, in the order they are checked
/// (after the request's own checks, <see cref="UploadChapterImageRequestValidator"/>): 404 NovelNotFound (no such
/// novel, or a deleted one), 403 NotOwner, 400 with the cover's codes when the file isn't a usable picture
/// (<see cref="CoverErrorCodes"/>), 400 UploadFailed when it can't be processed or stored. Nothing is saved besides the
/// file: the chapter save stores its address as an image paragraph.
/// </summary>
public class UploadChapterImageCommandHandler(
    ILogger<UploadChapterImageCommandHandler> logger,
    IUserContext userContext,
    INovelsRepository novelsRepository,
    IChapterImageService chapterImages) : IRequestHandler<UploadChapterImageCommand, UploadChapterImageResult>
{
    public async Task<UploadChapterImageResult> Handle(UploadChapterImageCommand request, CancellationToken cancellationToken)
    {
        var currentUser = userContext.GetCurrentUser() ?? throw new ForbidException("سجّل الدخول للمتابعة", "NotSignedIn");
        var novel = await novelsRepository.GetOne(request.NovelId) ?? throw new NotFoundException("الرواية غير موجودة", "NovelNotFound");
        if (novel.AuthorId != currentUser.Id)
        {
            throw new ForbidException("هذا الإجراء متاح لكاتب الرواية فقط", "NotOwner");
        }

        string url;
        try
        {
            await using var stream = request.Image.OpenReadStream();
            url = await chapterImages.StoreUploadAsync(novel.Id, stream, cancellationToken);
        }
        catch (CoverImageException ex)
        {
            logger.LogInformation("Refused a chapter picture for novel {NovelId}: {Code}", novel.Id, ex.Code);
            throw new BadRequestException(ex.Message, ex.Code);
        }
        // A cancelled request isn't a failed upload; a timeout inside the storage client is one.
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            logger.LogError(ex, "Could not store a chapter picture for novel {NovelId}", novel.Id);
            throw new BadRequestException(ChapterImages.UploadFailedMessage, ChapterImages.UploadFailedCode);
        }

        return new UploadChapterImageResult(url);
    }
}
