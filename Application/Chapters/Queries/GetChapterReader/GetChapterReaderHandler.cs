using Application.Chapters.DTOS;
using Application.Services;
using Application.Users;
using AutoMapper;
using Domain.Exceptions;
using Domain.Repositories;
using MediatR;
using Microsoft.Extensions.DependencyInjection;

namespace Application.Chapters.Queries.GetChapterReader;

public class GetChapterReaderHandler(
    IChaptersRepository chaptersRepository, 
    IChapterParagraphsRepository paragraphsRepository, 
    INovelsRepository novelsRepository, 
    IMapper mapper,
    IUserContext userContext,
    IPrivilegeService privilegeService,
    IServiceProvider serviceProvider) : IRequestHandler<GetChapterReaderQuery, ChapterSingleReaderDTO>
{
    public async Task<ChapterSingleReaderDTO> Handle(GetChapterReaderQuery request, CancellationToken cancellationToken)
    {
        var novel = await novelsRepository.GetOne(request.NovelId) ?? throw new NotFoundException("This novel wasn't found");
        var chapter = await chaptersRepository.GetChapterById(request.ChapterId) ?? throw new NotFoundException("This chapter wasn't found");
        
        var chapterDTO = mapper.Map<ChapterSingleReaderDTO>(chapter);
        chapterDTO.NextChapterSlug = await chaptersRepository.GetNextChapterSlug(request.NovelId, chapter.ChapterIndex);
        
        var currentUser = userContext.GetCurrentUser();
        
        // ✅ Authors can always read their own chapters (skip privilege check)
        if (currentUser != null && novel.AuthorId == currentUser.Id)
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
        
        // Fire-and-forget view count increment
        _ = IncrementViewsInBackground(chapter.Id);
        
        return chapterDTO;
    }

    private async Task IncrementViewsInBackground(Guid chapterId)
    {
        try
        {
            using var scope = serviceProvider.CreateScope();
            var backgroundChaptersRepository = scope.ServiceProvider.GetRequiredService<IChaptersRepository>();
            await backgroundChaptersRepository.IncrementChapterViewsCountAsync(chapterId);
        }
        catch
        {
            // Silently ignore - fire-and-forget
        }
    }
}
