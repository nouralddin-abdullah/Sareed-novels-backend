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
    private const string PublishedStatus = "Published";

    public async Task<ChapterSingleReaderDTO> Handle(GetChapterReaderQuery request, CancellationToken cancellationToken)
    {
        var novel = await novelsRepository.GetOne(request.NovelId) ?? throw new NotFoundException("This novel wasn't found");
        var chapter = await chaptersRepository.GetChapterById(request.ChapterId) ?? throw new NotFoundException("This chapter wasn't found");

        var currentUser = userContext.GetCurrentUser();
        var isAuthor = currentUser != null && novel.AuthorId == currentUser.Id;

        // Readers only get published chapters of published novels, through the novel they belong to.
        // (Authors can preview drafts of their own work.)
        if (chapter.NovelId != novel.Id || (!isAuthor && (novel.IsDraft || chapter.Status != PublishedStatus)))
        {
            throw new NotFoundException("This chapter wasn't found");
        }

        var chapterDTO = mapper.Map<ChapterSingleReaderDTO>(chapter);
        chapterDTO.NextChapterSlug = await chaptersRepository.GetNextChapterSlug(request.NovelId, chapter.ChapterIndex);

        if (isAuthor)
        {
            var authorParagraphs = await paragraphsRepository.GetChapterParagraphs(chapter.Id);
            chapterDTO.Paragraphs = mapper.Map<List<ChapterParagraphDTO>>(authorParagraphs);
            return chapterDTO;
        }

        // Check if chapter is locked by privilege system (for non-authors)
        var isLocked = await privilegeService.IsChapterLockedAsync(chapter.Id, currentUser?.Id);

        if (isLocked)
        {
            // Chapter is locked - don't return content
            chapterDTO.IsLocked = true;
            chapterDTO.LockMessage = "This chapter is locked by the privilege system. Subscribe to unlock all privilege chapters!";
            chapterDTO.Paragraphs = new List<ChapterParagraphDTO>(); // Empty paragraphs

            return chapterDTO;
        }

        // Chapter is unlocked - load paragraphs
        var paragraphs = await paragraphsRepository.GetChapterParagraphs(chapter.Id);
        chapterDTO.Paragraphs = mapper.Map<List<ChapterParagraphDTO>>(paragraphs);

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
            logger.LogWarning(ex, "Failed to track read for chapter {ChapterId} in background", chapterId);
        }
    }
}
