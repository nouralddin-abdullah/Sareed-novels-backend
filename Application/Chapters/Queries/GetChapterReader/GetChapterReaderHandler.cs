using Application.Chapters.DTOS;
using Application.Services;
using Application.Users;
using AutoMapper;
using Domain.Exceptions;
using Domain.Repositories;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Application.Chapters.Queries.GetChapterReader;

public class GetChapterReaderHandler(
    IChaptersRepository chaptersRepository,
    IChapterParagraphsRepository paragraphsRepository,
    INovelsRepository novelsRepository,
    IMapper mapper,
    IUserContext userContext,
    IVisitorContext visitorContext,
    IPrivilegeService privilegeService,
    IServiceScopeFactory scopeFactory,
    ILogger<GetChapterReaderHandler> logger) : IRequestHandler<GetChapterReaderQuery, ChapterSingleReaderDTO>
{
    /// <summary>What a reader is told about a privilege-locked chapter (clients show it as it is; isLocked is the flag).</summary>
    public const string LockMessage = "هذا الفصل ضمن الوصول المبكر. اشترك لتقرأ الفصول المقفلة كلها فور نشرها.";

    public async Task<ChapterSingleReaderDTO> Handle(GetChapterReaderQuery request, CancellationToken cancellationToken)
    {
        var novel = await novelsRepository.GetOne(request.NovelId) ?? throw new NotFoundException("الرواية غير موجودة", "NovelNotFound");
        var chapter = await chaptersRepository.GetChapterById(request.ChapterId) ?? throw new NotFoundException("الفصل غير موجود", "ChapterNotFound");

        var currentUser = userContext.GetCurrentUser();
        var isAuthor = currentUser != null && novel.AuthorId == currentUser.Id;

        if (!ChapterAccess.IsReadable(novel, chapter, isAuthor))
        {
            throw new NotFoundException("الفصل غير موجود", "ChapterNotFound");
        }

        var chapterDTO = mapper.Map<ChapterSingleReaderDTO>(chapter);
        chapterDTO.NextChapterSlug = await chaptersRepository.GetNextChapterSlug(request.NovelId, chapter.ChapterIndex);

        if (isAuthor)
        {
            var authorParagraphs = await paragraphsRepository.GetChapterParagraphs(chapter.Id);
            chapterDTO.Paragraphs = mapper.Map<List<ChapterParagraphDTO>>(authorParagraphs);
            return chapterDTO;
        }

        // Locked by early access for this reader (#94): no text, and when it opens to everyone.
        var earlyAccess = await privilegeService.GetViewAsync(novel.Id, novel.AuthorId, currentUser?.Id);
        if (earlyAccess.IsLockedForViewer(chapter))
        {
            chapterDTO.IsLocked = true;
            chapterDTO.LockMessage = LockMessage;
            chapterDTO.UnlocksAt = earlyAccess.UnlocksAtForViewer(chapter);
            chapterDTO.Paragraphs = new List<ChapterParagraphDTO>();

            return chapterDTO;
        }

        // Chapter is unlocked - load paragraphs
        var paragraphs = await paragraphsRepository.GetChapterParagraphs(chapter.Id);
        chapterDTO.Paragraphs = mapper.Map<List<ChapterParagraphDTO>>(paragraphs);

        // A download for offline reading isn't a read: the app sends POST .../view when the chapter is opened.
        if (!request.TrackView)
        {
            return chapterDTO;
        }

        // Resolve the visitor now: the background task outlives the request and can't read HttpContext.
        var visitorKey = visitorContext.GetVisitorKey();
        if (visitorKey != null)
        {
            _ = TrackReadInBackground(chapter.Id, novel.Id, visitorKey);
        }

        return chapterDTO;
    }

    private async Task TrackReadInBackground(Guid chapterId, Guid novelId, string visitorKey)
    {
        try
        {
            using var scope = scopeFactory.CreateScope();
            var viewTracking = scope.ServiceProvider.GetRequiredService<IViewTrackingService>();
            await viewTracking.TrackChapterView(chapterId, novelId, visitorKey);
        }
        catch (Exception ex)
        {
            // The reader already has the chapter; the read just isn't counted.
            logger.LogError(ex, "Failed to track read for chapter {ChapterId} in background", chapterId);
        }
    }
}
