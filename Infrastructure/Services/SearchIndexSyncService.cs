using Application.Services;
using Domain.Repositories;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Infrastructure.Services;

public class SearchIndexSyncService(
    IServiceProvider serviceProvider,
    ILogger<SearchIndexSyncService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("SearchIndexSyncService started");

        // Wait 30 seconds on startup to let the application initialize
        await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ProcessOutboxEntriesAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error processing search index outbox");
            }

            // Process every 30 seconds
            await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);
        }

        logger.LogInformation("SearchIndexSyncService stopped");
    }

    private async Task ProcessOutboxEntriesAsync(CancellationToken cancellationToken)
    {
        using var scope = serviceProvider.CreateScope();
        var outboxRepository = scope.ServiceProvider.GetRequiredService<ISearchIndexOutboxRepository>();
        var novelSearchService = scope.ServiceProvider.GetRequiredService<INovelSearchService>();
        var userSearchService = scope.ServiceProvider.GetRequiredService<IUserSearchService>();

        var pendingEntries = await outboxRepository.GetPendingEntriesAsync(100);

        if (pendingEntries.Count == 0)
        {
            return;
        }

        logger.LogInformation("Processing {Count} pending search index entries", pendingEntries.Count);

        foreach (var entry in pendingEntries)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                break;
            }

            try
            {
                bool success = false;

                if (entry.EntityType == "Novel")
                {
                    success = entry.Action switch
                    {
                        "Index" => await novelSearchService.IndexNovelAsync(entry.EntityId),
                        "Update" => await novelSearchService.UpdateNovelInIndexAsync(entry.EntityId),
                        "Delete" => await novelSearchService.DeleteNovelFromIndexAsync(entry.EntityId),
                        _ => false
                    };
                }
                else if (entry.EntityType == "User")
                {
                    // Get userId from ErrorMessage field (temporary solution)
                    var userId = entry.ErrorMessage ?? entry.EntityId.ToString();
                    
                    success = entry.Action switch
                    {
                        "Index" => await userSearchService.IndexUserAsync(userId),
                        "Update" => await userSearchService.UpdateUserInIndexAsync(userId),
                        "Delete" => await userSearchService.DeleteUserFromIndexAsync(userId),
                        _ => false
                    };
                }

                if (success)
                {
                    await outboxRepository.MarkAsProcessedAsync(entry.Id);
                    logger.LogDebug(
                        "Processed outbox entry {EntryId}: {Action} {EntityType} {EntityId}",
                        entry.Id,
                        entry.Action,
                        entry.EntityType,
                        entry.EntityId
                    );
                }
                else
                {
                    await outboxRepository.IncrementRetryCountAsync(entry.Id);
                    await outboxRepository.MarkAsFailedAsync(
                        entry.Id,
                        $"Action {entry.Action} failed for {entry.EntityType} {entry.EntityId}"
                    );

                    logger.LogWarning(
                        "Failed to process outbox entry {EntryId} (retry {RetryCount}/5)",
                        entry.Id,
                        entry.RetryCount + 1
                    );
                }

                // Small delay between processing to avoid overwhelming Elasticsearch
                await Task.Delay(100, cancellationToken);
            }
            catch (Exception ex)
            {
                logger.LogError(
                    ex,
                    "Error processing outbox entry {EntryId}: {Action} {EntityType} {EntityId}",
                    entry.Id,
                    entry.Action,
                    entry.EntityType,
                    entry.EntityId
                );

                await outboxRepository.IncrementRetryCountAsync(entry.Id);
                await outboxRepository.MarkAsFailedAsync(entry.Id, ex.Message);
            }
        }

        logger.LogInformation("Completed processing search index outbox batch");
    }
}
