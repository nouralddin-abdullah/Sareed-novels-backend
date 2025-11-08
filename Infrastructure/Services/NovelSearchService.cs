using Application.Common;
using Application.Search.DTOs;
using Application.Services;
using Domain.Repositories;
using Infrastructure.Configuration;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Nest;

namespace Infrastructure.Services;

public class NovelSearchService(
    IElasticClient elasticClient,
    INovelsRepository novelsRepository,
    ILogger<NovelSearchService> logger,
    IOptions<ElasticsearchSettings> settings) : INovelSearchService
{
    private readonly string _indexName = settings.Value.NovelIndexName;

    public async Task<PagedResult<NovelSearchResult>> SearchNovelsAsync(
        SearchNovelsRequest request,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var searchDescriptor = new SearchDescriptor<NovelSearchDocument>()
                .Index(_indexName)
                .From((request.PageNumber - 1) * request.PageSize)
                .Size(request.PageSize)
                .Query(q => BuildSearchQuery(q, request))
                .Sort(s => BuildSortQuery(s, request.SortBy))
                .TrackTotalHits(true);

            var response = await elasticClient.SearchAsync<NovelSearchDocument>(searchDescriptor, cancellationToken);

            if (!response.IsValid)
            {
                logger.LogError("Elasticsearch search failed: {Error}", response.DebugInformation);
                return new PagedResult<NovelSearchResult>(
                    new List<NovelSearchResult>(),
                    0,
                    request.PageSize,
                    request.PageNumber
                );
            }

            var results = response.Documents.Select(doc => new NovelSearchResult
            {
                Id = doc.Id,
                Title = doc.Title,
                Slug = doc.Slug,
                Summary = doc.Summary,
                CoverImageUrl = doc.CoverImageUrl,
                Status = doc.Status,
                Genres = doc.Genres,
                ChapterCount = doc.ChapterCount,
                TotalAverageScore = doc.TotalAverageScore,
                ReviewCount = doc.ReviewCount,
                TotalViews = doc.TotalViews,
                CreatedAt = doc.CreatedAt,
                LastUpdatedAt = doc.LastUpdatedAt
            }).ToList();

            var totalCount = response.Total;

            logger.LogInformation(
                "Search completed: query={Query}, total={Total}, page={Page}",
                request.Query ?? "all",
                totalCount,
                request.PageNumber
            );

            return new PagedResult<NovelSearchResult>(
                results,
                (int)totalCount,
                request.PageSize,
                request.PageNumber
            );
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error performing novel search");
            return new PagedResult<NovelSearchResult>(
                new List<NovelSearchResult>(),
                0,
                request.PageSize,
                request.PageNumber
            );
        }
    }

    public async Task<bool> IndexNovelAsync(Guid novelId)
    {
        try
        {
            var novel = await novelsRepository.GetOne(novelId);
            if (novel == null)
            {
                logger.LogWarning("Novel {NovelId} not found for indexing", novelId);
                return false;
            }

            // Only skip drafts - IsEligibleForRanking is for ranking only, not search
            if (novel.IsDraft)
            {
                logger.LogInformation(
                    "Skipping indexing for draft novel {NovelId}",
                    novelId
                );
                return true; // Not an error, just not eligible
            }

            var document = MapNovelToDocument(novel);
            var response = await elasticClient.IndexAsync(document, idx => idx.Index(_indexName));

            if (!response.IsValid)
            {
                logger.LogError(
                    "Failed to index novel {NovelId}: {Error}",
                    novelId,
                    response.DebugInformation
                );
                return false;
            }

            logger.LogInformation("Successfully indexed novel {NovelId}", novelId);
            return true;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error indexing novel {NovelId}", novelId);
            return false;
        }
    }

    public async Task<bool> UpdateNovelInIndexAsync(Guid novelId)
    {
        try
        {
            var novel = await novelsRepository.GetOne(novelId);
            if (novel == null)
            {
                logger.LogWarning("Novel {NovelId} not found for updating", novelId);
                // Novel was deleted, remove from index
                return await DeleteNovelFromIndexAsync(novelId);
            }

            // If novel becomes draft, remove from index
            if (novel.IsDraft)
            {
                logger.LogInformation(
                    "Novel {NovelId} is now draft, removing from index",
                    novelId
                );
                return await DeleteNovelFromIndexAsync(novelId);
            }

            var document = MapNovelToDocument(novel);
            var response = await elasticClient.UpdateAsync<NovelSearchDocument>(
                novelId.ToString(),
                u => u
                    .Index(_indexName)
                    .Doc(document)
                    .DocAsUpsert(true) // Create if doesn't exist
            );

            if (!response.IsValid)
            {
                logger.LogError(
                    "Failed to update novel {NovelId}: {Error}",
                    novelId,
                    response.DebugInformation
                );
                return false;
            }

            logger.LogInformation("Successfully updated novel {NovelId} in index", novelId);
            return true;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error updating novel {NovelId} in index", novelId);
            return false;
        }
    }

    public async Task<bool> DeleteNovelFromIndexAsync(Guid novelId)
    {
        try
        {
            var response = await elasticClient.DeleteAsync<NovelSearchDocument>(
                novelId.ToString(),
                d => d.Index(_indexName)
            );

            if (!response.IsValid && response.Result != Result.NotFound)
            {
                logger.LogError(
                    "Failed to delete novel {NovelId}: {Error}",
                    novelId,
                    response.DebugInformation
                );
                return false;
            }

            logger.LogInformation("Successfully deleted novel {NovelId} from index", novelId);
            return true;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error deleting novel {NovelId} from index", novelId);
            return false;
        }
    }

    public async Task<bool> BulkIndexNovelsAsync(IEnumerable<Guid> novelIds)
    {
        try
        {
            var novelIdsList = novelIds.ToList();
            logger.LogInformation("Starting bulk indexing of {Count} novels", novelIdsList.Count);

            var documents = new List<NovelSearchDocument>();

            foreach (var novelId in novelIdsList)
            {
                var novel = await novelsRepository.GetOne(novelId);
                if (novel != null && !novel.IsDraft)
                {
                    documents.Add(MapNovelToDocument(novel));
                }
            }

            if (documents.Count == 0)
            {
                logger.LogInformation("No eligible novels to index");
                return true;
            }

            var bulkResponse = await elasticClient.BulkAsync(b => b
                .Index(_indexName)
                .IndexMany(documents)
            );

            if (!bulkResponse.IsValid)
            {
                logger.LogError("Bulk indexing failed: {Error}", bulkResponse.DebugInformation);
                return false;
            }

            logger.LogInformation(
                "Successfully bulk indexed {Count} novels",
                documents.Count
            );
            return true;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error during bulk indexing");
            return false;
        }
    }

    public async Task<int> ReindexAllNovelsAsync()
    {
        try
        {
            logger.LogInformation("Starting full reindex of all novels");

            // Get all novels (only non-drafts will be indexed)
            var (novels, totalCount) = await novelsRepository.GetLatestNovels(int.MaxValue, 1);
            var eligibleNovels = novels
                .Where(n => !n.IsDraft)
                .ToList();

            logger.LogInformation(
                "Found {Total} total novels, {Eligible} eligible for indexing",
                totalCount,
                eligibleNovels.Count
            );

            var indexedCount = 0;
            var batchSize = 1000;

            for (int i = 0; i < eligibleNovels.Count; i += batchSize)
            {
                var batch = eligibleNovels.Skip(i).Take(batchSize).ToList();
                var documents = batch.Select(MapNovelToDocument).ToList();

                var bulkResponse = await elasticClient.BulkAsync(b => b
                    .Index(_indexName)
                    .IndexMany(documents)
                );

                if (bulkResponse.IsValid)
                {
                    indexedCount += documents.Count;
                    logger.LogInformation(
                        "Indexed batch {Current}/{Total}",
                        i + batch.Count,
                        eligibleNovels.Count
                    );
                }
                else
                {
                    logger.LogError("Batch indexing failed: {Error}", bulkResponse.DebugInformation);
                }

                // Small delay to avoid overwhelming Elasticsearch
                await Task.Delay(100);
            }

            logger.LogInformation(
                "Reindexing completed: {Count} novels indexed",
                indexedCount
            );
            return indexedCount;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error during full reindexing");
            return 0;
        }
    }

    public async Task<bool> EnsureIndexExistsAsync()
    {
        try
        {
            var existsResponse = await elasticClient.Indices.ExistsAsync(_indexName);
            if (existsResponse.Exists)
            {
                logger.LogInformation("Index {IndexName} already exists", _indexName);
                return true;
            }

            logger.LogInformation("Creating index {IndexName}", _indexName);

            var createResponse = await elasticClient.Indices.CreateAsync(_indexName, c => c
                .Settings(s => s
                    .Analysis(a => a
                        .Analyzers(an => an
                            .Custom("arabic_custom", ca => ca
                                .Tokenizer("standard")
                                .Filters("lowercase", "arabic_normalization", "arabic_stem")
                            )
                        )
                    )
                )
                .Map<NovelSearchDocument>(m => m
                    .Properties(p => p
                        .Keyword(k => k.Name(n => n.Id))
                        .Text(t => t
                            .Name(n => n.Title)
                            .Analyzer("arabic_custom")
                            .Fields(f => f.Keyword(k => k.Name("keyword")))
                        )
                        .Keyword(k => k.Name(n => n.Slug))
                        .Text(t => t.Name(n => n.Summary))
                        .Keyword(k => k.Name(n => n.CoverImageUrl))
                        .Keyword(k => k.Name(n => n.Status))
                        .Boolean(b => b.Name(n => n.IsDraft))
                        .Boolean(b => b.Name(n => n.IsEligibleForRanking))
                        .Keyword(k => k.Name(n => n.Genres))
                        .Number(n => n.Name(nn => nn.ChapterCount).Type(NumberType.Integer))
                        .Number(n => n.Name(nn => nn.TotalAverageScore).Type(NumberType.Float))
                        .Number(n => n.Name(nn => nn.ReviewCount).Type(NumberType.Integer))
                        .Number(n => n.Name(nn => nn.TotalViews).Type(NumberType.Integer))
                        .Date(d => d.Name(n => n.CreatedAt))
                        .Date(d => d.Name(n => n.LastUpdatedAt))
                    )
                )
            );

            if (!createResponse.IsValid)
            {
                logger.LogError("Failed to create index: {Error}", createResponse.DebugInformation);
                return false;
            }

            logger.LogInformation("Successfully created index {IndexName}", _indexName);
            return true;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error ensuring index exists");
            return false;
        }
    }

    private QueryContainer BuildSearchQuery(QueryContainerDescriptor<NovelSearchDocument> q, SearchNovelsRequest request)
    {
        var mustClauses = new List<QueryContainer>();

        // Only filter out drafts - IsEligibleForRanking is for ranking only, not search
        mustClauses.Add(q.Term(t => t.Field(f => f.IsDraft).Value(false)));

        // Text search query
        if (!string.IsNullOrWhiteSpace(request.Query))
        {
            mustClauses.Add(q.Match(m => m
                .Field(f => f.Title)
                .Query(request.Query)
                .Fuzziness(Fuzziness.Auto)
            ));
        }

        // Genre filter
        if (request.Genres != null && request.Genres.Any())
        {
            mustClauses.Add(q.Terms(t => t
                .Field(f => f.Genres)
                .Terms(request.Genres)
            ));
        }

        // Status filter
        if (!string.IsNullOrWhiteSpace(request.Status))
        {
            mustClauses.Add(q.Term(t => t.Field(f => f.Status).Value(request.Status)));
        }

        // Chapter count range filter - support multiple ranges with OR logic
        var rangesToUse = new List<ChapterCountRange>();
        
        // Prioritize ChapterRanges (new multiple ranges)
        if (request.ChapterRanges != null && request.ChapterRanges.Any())
        {
            rangesToUse.AddRange(request.ChapterRanges);
        }
        // Fallback to single ChapterRange for backward compatibility
        else if (request.ChapterRange.HasValue)
        {
            rangesToUse.Add(request.ChapterRange.Value);
        }

        if (rangesToUse.Any())
        {
            // If only one range, use simple query
            if (rangesToUse.Count == 1)
            {
                var rangeQuery = BuildChapterRangeQuery(q, rangesToUse[0]);
                if (rangeQuery != null)
                {
                    mustClauses.Add(rangeQuery);
                }
            }
            else
            {
                // Multiple ranges: use OR logic (should clause)
                var rangeQueries = rangesToUse
                    .Select(range => BuildChapterRangeQuery(q, range))
                    .Where(rq => rq != null)
                    .ToList();

                if (rangeQueries.Any())
                {
                    mustClauses.Add(q.Bool(b => b.Should(rangeQueries.ToArray()).MinimumShouldMatch(1)));
                }
            }
        }

        return q.Bool(b => b.Must(mustClauses.ToArray()));
    }

    private QueryContainer? BuildChapterRangeQuery(QueryContainerDescriptor<NovelSearchDocument> q, ChapterCountRange range)
    {
        return range switch
        {
            ChapterCountRange.Range_1_10 => q.Range(r => r
                .Field(f => f.ChapterCount)
                .GreaterThanOrEquals(1)
                .LessThanOrEquals(10)
            ),
            ChapterCountRange.Range_10_20 => q.Range(r => r
                .Field(f => f.ChapterCount)
                .GreaterThan(10)
                .LessThanOrEquals(20)
            ),
            ChapterCountRange.Range_20_50 => q.Range(r => r
                .Field(f => f.ChapterCount)
                .GreaterThan(20)
                .LessThanOrEquals(50)
            ),
            ChapterCountRange.Range_50_Plus => q.Range(r => r
                .Field(f => f.ChapterCount)
                .GreaterThan(50)
            ),
            _ => null
        };
    }

    private IPromise<IList<ISort>> BuildSortQuery(SortDescriptor<NovelSearchDocument> s, NovelSortBy sortBy)
    {
        return sortBy switch
        {
            NovelSortBy.Newest => s.Descending(f => f.CreatedAt),
            NovelSortBy.LastUpdated => s.Descending(f => f.LastUpdatedAt),
            NovelSortBy.MostPopular => s.Descending(f => f.TotalViews),
            NovelSortBy.HighestRated => s.Descending(f => f.TotalAverageScore),
            NovelSortBy.MostReviewed => s.Descending(f => f.ReviewCount),
            _ => s.Descending(SortSpecialField.Score) // Relevance (default)
        };
    }

    private NovelSearchDocument MapNovelToDocument(Domain.Entities.Novel novel)
    {
        return new NovelSearchDocument
        {
            Id = novel.Id,
            Title = novel.Title,
            Slug = novel.Slug,
            Summary = novel.Summary ?? string.Empty,
            CoverImageUrl = novel.CoverImageUrl,
            Status = novel.Status,
            IsDraft = novel.IsDraft,
            IsEligibleForRanking = novel.IsEligibleForRanking,
            Genres = novel.NovelGenres?.Select(ng => ng.Genre.Name).ToList() ?? new List<string>(),
            ChapterCount = novel.ChapterCount,
            TotalAverageScore = novel.TotalAverageScore,
            ReviewCount = novel.ReviewCount,
            TotalViews = novel.TotalViews,
            CreatedAt = novel.CreatedAt,
            LastUpdatedAt = novel.LastUpdatedAt
        };
    }
}
