using Application.Chapters.DTOS;
using Application.Chapters.Queries.GetChaptersAuthor;
using Application.Services;
using Application.Users;
using AutoMapper;
using Domain.Exceptions;
using Domain.Repositories;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Application.Chapters.Commands.DeleteChapter;

public class DeleteChapterCommandHandler(
    ILogger<DeleteChapterCommandHandler> logger, 
    INovelsRepository novelsRepository, 
    IChaptersRepository chaptersRepository, 
    IUserContext userContext,
    IChapterSequenceService sequenceService,
    ISearchIndexQueueService searchIndexQueue,
    IServiceProvider serviceProvider) : IRequestHandler<DeleteChapterCommand, bool>
{
    public async Task<bool> Handle(DeleteChapterCommand request, CancellationToken cancellationToken)
    {
        logger.LogInformation("Deleting chapter {@chapter}", request);
        var currentUser = userContext.GetCurrentUser() ?? throw new ForbidException("User not signed in");
        var novel = await novelsRepository.GetOne(request.NovelId) ?? throw new NotFoundException("This novel wasn't found");
        var chapter = await chaptersRepository.GetChapterById(request.ChapterId) ?? throw new  NotFoundException("Chapter wasn't found");
        
        if (novel.AuthorId != currentUser.Id) throw new ForbidException("User doesn't own this novel");
        
        var wasPublished = chapter.Status == "Published";
        var publishedSequence = chapter.PublishedChapterSequence;
        
        var deleteResult = await chaptersRepository.DeleteChapter(chapter);
        if (deleteResult)
        {
            novel.ChapterCount--;
            await novelsRepository.UpdateOne(novel);
            
            // If deleted chapter was Published, recalculate sequences
            if (wasPublished)
            {
                logger.LogInformation(
                    "Published chapter {ChapterId} deleted from novel {NovelId}, triggering sequence recalculation", 
                    request.ChapterId, request.NovelId);
                
                await sequenceService.RecalculateSequencesForNovelAsync(request.NovelId);
                await sequenceService.UpdateReadingProgressForNovelAsync(request.NovelId);
                
                // Trigger privilege update (decrease locked count)
                if (publishedSequence.HasValue)
                {
                    var privilegeService = serviceProvider.GetRequiredService<IPrivilegeService>();
                    await privilegeService.OnChapterDeletedAsync(request.NovelId, publishedSequence.Value);
                }
            }
            
            // Queue for Elasticsearch update (ChapterCount changed)
            await searchIndexQueue.QueueUpdateAsync(request.NovelId);
            logger.LogDebug("Queued novel {NovelId} for search index update (chapter deleted)", request.NovelId);
        }
        
        return deleteResult;
    }
}
