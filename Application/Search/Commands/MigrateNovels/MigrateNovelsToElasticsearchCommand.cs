using Application.Services;
using Application.Users.Commands.FollowUser;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Application.Search.Commands.MigrateNovels;

public record MigrateNovelsToElasticsearchCommand : IRequest<OperationResult>;

public class MigrateNovelsToElasticsearchCommandHandler(
    ILogger<MigrateNovelsToElasticsearchCommandHandler> logger,
    INovelSearchService searchService) : IRequestHandler<MigrateNovelsToElasticsearchCommand, OperationResult>
{
    public async Task<OperationResult> Handle(
        MigrateNovelsToElasticsearchCommand request,
        CancellationToken cancellationToken)
    {
        logger.LogInformation("Starting migration of all novels to Elasticsearch");

        // Ensure index exists
        var indexExists = await searchService.EnsureIndexExistsAsync();
        if (!indexExists)
        {
            return new OperationResult
            {
                Success = false,
                Message = "Failed to create Elasticsearch index"
            };
        }

        // Reindex all novels
        var indexedCount = await searchService.ReindexAllNovelsAsync();

        logger.LogInformation(
            "Migration completed: {Count} novels indexed",
            indexedCount
        );

        return new OperationResult
        {
            Success = true,
            Message = $"Successfully indexed {indexedCount} novels"
        };
    }
}
