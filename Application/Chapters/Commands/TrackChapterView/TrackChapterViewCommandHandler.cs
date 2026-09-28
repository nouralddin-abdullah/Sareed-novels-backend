using Application.Services;
using Application.Users;
using Domain.Exceptions;
using Domain.Repositories;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Application.Chapters.Commands.TrackChapterView;

/// <summary>
/// Counts a read the way opening the chapter in the reader does (GetChapterReaderHandler), with the same visitor key
/// and the same once-a-day rule: a chapter readers can't open is a 404 as there, and nothing is counted for the
/// novel's author, a chapter locked for the caller (the reader would have given them no text), or a crawler. The
/// count is awaited, so a failure reaches the app, which keeps the view queued and sends it again.
/// </summary>
public class TrackChapterViewCommandHandler(
    IChaptersRepository chaptersRepository,
    INovelsRepository novelsRepository,
    IUserContext userContext,
    IVisitorContext visitorContext,
    IPrivilegeService privilegeService,
    IViewTrackingService viewTracking,
    ILogger<TrackChapterViewCommandHandler> logger) : IRequestHandler<TrackChapterViewCommand, bool>
{
    public async Task<bool> Handle(TrackChapterViewCommand request, CancellationToken cancellationToken)
    {
        var novel = await novelsRepository.GetOne(request.NovelId) ?? throw new NotFoundException("الرواية غير موجودة", "NovelNotFound");
        var chapter = await chaptersRepository.GetChapterById(request.ChapterId) ?? throw new NotFoundException("الفصل غير موجود", "ChapterNotFound");

        var currentUser = userContext.GetCurrentUser();
        var isAuthor = currentUser != null && novel.AuthorId == currentUser.Id;
        if (!ChapterAccess.IsReadable(novel, chapter, isAuthor))
        {
            throw new NotFoundException("الفصل غير موجود", "ChapterNotFound");
        }

        // As in the reader: the author's own previews never count, nor a chapter locked for the caller.
        if (isAuthor || await privilegeService.IsChapterLockedAsync(chapter.Id, currentUser?.Id))
        {
            return false;
        }

        var visitorKey = visitorContext.GetVisitorKey();
        if (visitorKey == null)
        {
            return false;
        }

        var counted = await viewTracking.TrackChapterView(chapter.Id, novel.Id, visitorKey, cancellationToken);
        logger.LogDebug("Offline read of chapter {ChapterId} {Outcome}", chapter.Id, counted ? "counted" : "already counted today");
        return counted;
    }
}
