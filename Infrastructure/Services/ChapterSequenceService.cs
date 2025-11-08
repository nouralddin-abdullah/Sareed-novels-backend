using Application.Services;
using Domain.Repositories;
using Microsoft.Extensions.Logging;

namespace Infrastructure.Services;

public class ChapterSequenceService(
    ILogger<ChapterSequenceService> logger,
    INovelsRepository novelsRepository,
    IChaptersRepository chaptersRepository) : IChapterSequenceService
{
    public async Task RecalculateSequencesForNovelAsync(Guid novelId)
    {
        logger.LogInformation("Starting sequence recalculation for novel {NovelId}", novelId);
        
        var updatedCount = await novelsRepository.RecalculatePublishedSequencesAsync(novelId);
        
        logger.LogInformation("Recalculated sequences for {Count} published chapters in novel {NovelId}", 
            updatedCount, novelId);
    }

    public async Task UpdateReadingProgressForNovelAsync(Guid novelId)
    {
        logger.LogInformation("Updating reading progress for novel {NovelId} after sequence change", novelId);
        
        // Get all published chapters with their new sequences
        var publishedChapters = await chaptersRepository.GetChaptersReaderView(novelId);
        var publishedChaptersList = publishedChapters.OrderBy(c => c.ChapterIndex).ToList();
        
        if (!publishedChaptersList.Any())
        {
            logger.LogWarning("No published chapters found for novel {NovelId}", novelId);
            return;
        }

        // Create a mapping of chapter IDs to their new sequence numbers
        var chapterSequenceMap = publishedChaptersList
            .Select((chapter, index) => new { chapter.Id, Sequence = index + 1 })
            .ToDictionary(x => x.Id, x => x.Sequence);

        logger.LogInformation("Sequence map created with {Count} chapters for novel {NovelId}", 
            chapterSequenceMap.Count, novelId);
        
        // Note: We don't automatically update UserNovelProgress here because:
        // 1. It's tracked per user and could affect many users
        // 2. We use PublishedChapterSequence from Chapter entity as the source of truth
        // 3. TrackProgress handler will use the cached sequence when user reads
        // 4. GetLibrary will recalculate on-the-fly if needed
        
        // In the future, we could add a batch job to fix orphaned progress entries
    }
}
