using Application.Services;
using Application.Users.Commands.FollowUser;
using Domain.Repositories;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Application.Library.Commands.MigrateSequences;

public record MigratePublishedSequencesCommand : IRequest<OperationResult>;

public class MigratePublishedSequencesCommandHandler(
    ILogger<MigratePublishedSequencesCommandHandler> logger,
    INovelsRepository novelsRepository,
    IChapterSequenceService sequenceService) : IRequestHandler<MigratePublishedSequencesCommand, OperationResult>
{
    public async Task<OperationResult> Handle(MigratePublishedSequencesCommand request, CancellationToken cancellationToken)
    {
        logger.LogInformation("Starting migration of PublishedChapterSequence for all novels");
        
        var (novels, _) = await novelsRepository.GetLatestNovels(int.MaxValue, 1);
        var novelsList = novels.ToList();
        
        logger.LogInformation("Found {Count} novels to process", novelsList.Count);
        
        int processedCount = 0;
        int errorCount = 0;
        
        foreach (var novel in novelsList)
        {
            try
            {
                await sequenceService.RecalculateSequencesForNovelAsync(novel.Id);
                processedCount++;
                
                if (processedCount % 10 == 0)
                {
                    logger.LogInformation("Processed {Count}/{Total} novels", processedCount, novelsList.Count);
                }
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error processing novel {NovelId}", novel.Id);
                errorCount++;
            }
        }
        
        logger.LogInformation(
            "Migration completed. Processed: {ProcessedCount}, Errors: {ErrorCount}", 
            processedCount, errorCount);
        
        return new OperationResult
        {
            Success = errorCount == 0,
            Message = $"Migration completed. Processed: {processedCount} novels, Errors: {errorCount}"
        };
    }
}
