using Application.Chapters.Paragraphs;
using Domain.Entities;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Infrastructure.BackgroundJobs;

/// <summary>
/// Counts the words of the chapters that have no count (#77: <see cref="Chapter.WordsCount"/> null, chapters from before
/// word counts), once, in the background after the app starts. A batch of chapters at a time, each counted from its
/// stored paragraphs (their content, kind and caption) as the API serves them, as creating and saving count
/// (<see cref="ChapterWords"/>), and stored only while the chapter still has no count, so a save meanwhile keeps its
/// own: a save of the text writes its new count, and one that doesn't touch the text writes none. Running it again changes nothing; it stops when every chapter has
/// a count. A failure is logged and the backfill starts again a minute later; one cut short by a restart goes on at the
/// next start. Until a chapter is counted, the author's list shows its <c>wordsCount</c> as null, and its novel's too.
/// </summary>
public sealed class ChapterWordsBackfillService(
    IServiceScopeFactory scopeFactory,
    TimeProvider timeProvider,
    ILogger<ChapterWordsBackfillService> logger) : BackgroundService
{
    /// <summary>Chapters read at a time: their paragraphs are loaded together (a chapter's text is up to 100,000 characters).</summary>
    internal const int BatchSize = 25;

    private static readonly TimeSpan RetryDelay = TimeSpan.FromMinutes(1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Yield(); // don't hold up app startup

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var counted = await BackfillAsync(stoppingToken);
                if (counted > 0)
                {
                    logger.LogInformation("Counted the words of {Count} chapters from before word counts", counted);
                }
                return;
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                logger.LogError(ex, "Counting the words of chapters from before word counts failed; retrying in {Delay}", RetryDelay);
            }

            try
            {
                await Task.Delay(RetryDelay, timeProvider, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    /// <summary>Counts every chapter that has no count yet; answers how many it stored.</summary>
    internal async Task<int> BackfillAsync(CancellationToken cancellationToken)
    {
        var stored = 0;
        Guid? after = null;
        while (true)
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

            // In id order from where the last batch ended, so a chapter left uncounted (deleted meanwhile) isn't read again.
            var uncounted = db.Chapters.AsNoTracking().Where(c => c.WordsCount == null);
            if (after is { } last)
            {
                uncounted = uncounted.Where(c => c.Id.CompareTo(last) > 0);
            }
            var ids = await uncounted.OrderBy(c => c.Id).Select(c => c.Id).Take(BatchSize).ToListAsync(cancellationToken);
            if (ids.Count == 0)
            {
                return stored;
            }

            var paragraphs = (await db.ChapterParagraphs
                    .AsNoTracking()
                    .Where(p => ids.Contains(p.ChapterId))
                    .Select(p => new ChapterParagraph
                    {
                        ChapterId = p.ChapterId, Content = p.Content, ContentType = p.ContentType, Caption = p.Caption
                    })
                    .ToListAsync(cancellationToken))
                .ToLookup(p => p.ChapterId);

            foreach (var id in ids)
            {
                var words = ChapterWords.Count(paragraphs[id]);
                stored += await db.Chapters
                    .Where(c => c.Id == id && c.WordsCount == null)
                    .ExecuteUpdateAsync(s => s.SetProperty(c => c.WordsCount, words), cancellationToken);
            }

            after = ids[^1];
        }
    }
}
