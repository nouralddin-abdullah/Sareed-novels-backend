using Application.Chapters.DTOS;
using Application.Chapters.Queries.GetChaptersAuthor;
using Application.Services;
using Application.Users;
using AutoMapper;
using Domain.Exceptions;
using Domain.Repositories;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Application.Chapters.Commands.DeleteChapter;

public class DeleteChapterCommandHandler(
    ILogger<DeleteChapterCommandHandler> logger, 
    INovelsRepository novelsRepository, 
    IChaptersRepository chaptersRepository, 
    IUserContext userContext,
    IChapterSequenceService sequenceService) : IRequestHandler<DeleteChapterCommand, bool>
{
    public async Task<bool> Handle(DeleteChapterCommand request, CancellationToken cancellationToken)
    {
        logger.LogInformation("Deleting chapter {@chapter}", request);
        var currentUser = userContext.GetCurrentUser() ?? throw new ForbidException("سجّل الدخول للمتابعة", "NotSignedIn");
        var novel = await novelsRepository.GetOne(request.NovelId) ?? throw new NotFoundException("الرواية غير موجودة", "NovelNotFound");
        var chapter = await chaptersRepository.GetChapterById(request.ChapterId) ?? throw new NotFoundException("الفصل غير موجود", "ChapterNotFound");
        
        if (novel.AuthorId != currentUser.Id) throw new ForbidException("هذا الإجراء متاح لكاتب الرواية فقط", "NotOwner");

        // The chapter must belong to the novel the caller owns; otherwise any author could delete any chapter.
        if (chapter.NovelId != novel.Id) throw new NotFoundException("الفصل غير موجود", "ChapterNotFound");
        
        var wasPublished = chapter.Status == "Published";
        
        var deleteResult = await chaptersRepository.DeleteChapter(chapter);
        if (deleteResult)
        {
            await novelsRepository.RefreshChapterCountAsync(novel.Id);
            
            // If deleted chapter was Published, recalculate sequences
            if (wasPublished)
            {
                logger.LogInformation(
                    "Published chapter {ChapterId} deleted from novel {NovelId}, triggering sequence recalculation", 
                    request.ChapterId, request.NovelId);
                
                // The other chapters keep their early-access locks (#94): a lock is the chapter's own, not a position.
                await sequenceService.RecalculateSequencesForNovelAsync(request.NovelId);
                await sequenceService.UpdateReadingProgressForNovelAsync(request.NovelId);
            }
            
        }
        
        return deleteResult;
    }
}
