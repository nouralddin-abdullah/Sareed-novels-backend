using Application.Common;
using Application.Entities.DTOs;
using Application.Search.DTOs;
using Application.Services;
using Domain.Entities;
using Domain.Repositories;
using Infrastructure.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OpenSearch.Client; // ✅ Changed from Nest
using System.Text.Json;

namespace Infrastructure.Services;

// ✅ Entity (Character/Location/Organization) search service
public class EntitySearchService(
    ILogger<EntitySearchService> logger,
    IOpenSearchClient openSearchClient, // ✅ Changed from IElasticClient
    INovelEntityRepository entityRepository,
    INovelsRepository novelsRepository, // Used for potential future features
    IOptions<OpenSearchSettings> settings) : IEntitySearchService // Used for advanced configuration
{
    private readonly string _indexName = "sareed-entities";

    public async Task<PagedResult<EntityListDTO>> SearchEntitiesAsync(
        Guid novelId,
        string? query = null,
        string? section = null,
        int pageNumber = 1,
        int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var searchDescriptor = new SearchDescriptor<NovelEntitySearchDocument>()
                .Index(_indexName)
                .From((pageNumber - 1) * pageSize)
                .Size(pageSize)
                .Query(q => BuildSearchQuery(q, novelId, query, section))
                .TrackTotalHits(true);

            var response = await openSearchClient.SearchAsync<NovelEntitySearchDocument>(searchDescriptor, cancellationToken);

            if (!response.IsValid)
            {
                logger.LogError("Entity search failed: {Error}", response.DebugInformation);
                return new PagedResult<EntityListDTO>(
                    new List<EntityListDTO>(),
                    0,
                    pageSize,
                    pageNumber
                );
            }

            var results = response.Documents.Select(doc => new EntityListDTO
            {
                Id = Guid.Parse(doc.Id),
                Section = doc.Section,
                Icon = doc.Icon,
                Name = doc.Name,
                ShortDescription = doc.ShortDescription,
                ImageUrl = doc.ImageUrl,
                CreatedAt = DateTime.UtcNow,
                ArticlesCount = 0,
                RelationshipsCount = 0
            }).ToList();

            var totalCount = response.Total;

            logger.LogInformation(
                "Entity search completed: novel={NovelId}, query={Query}, total={Total}",
                novelId,
                query ?? "all",
                totalCount
            );

            return new PagedResult<EntityListDTO>(
                results,
                (int)totalCount,
                pageSize,
                pageNumber
            );
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error performing entity search for novel {NovelId}", novelId);
            return new PagedResult<EntityListDTO>(
                new List<EntityListDTO>(),
                0,
                pageSize,
                pageNumber
            );
        }
    }

    public async Task<bool> IndexEntityAsync(Guid entityId)
    {
        try
        {
            var entity = await entityRepository.GetEntityByIdAsync(entityId);
            if (entity == null)
            {
                logger.LogWarning("Entity {EntityId} not found for indexing", entityId);
                return false;
            }

            if (entity.IsDeleted)
            {
                logger.LogInformation("Skipping deleted entity {EntityId}", entityId);
                return await DeleteEntityFromIndexAsync(entityId);
            }

            var document = MapEntityToDocument(entity);
            var response = await openSearchClient.IndexAsync(document, idx => idx.Index(_indexName));

            if (!response.IsValid)
            {
                logger.LogError("Failed to index entity {EntityId}: {Error}", entityId, response.DebugInformation);
                return false;
            }

            logger.LogInformation("Successfully indexed entity {EntityId}", entityId);
            return true;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error indexing entity {EntityId}", entityId);
            return false;
        }
    }

    public async Task<bool> UpdateEntityInIndexAsync(Guid entityId)
    {
        try
        {
            var entity = await entityRepository.GetEntityByIdAsync(entityId);
            if (entity == null)
            {
                logger.LogWarning("Entity {EntityId} not found for updating", entityId);
                return await DeleteEntityFromIndexAsync(entityId);
            }

            if (entity.IsDeleted)
            {
                return await DeleteEntityFromIndexAsync(entityId);
            }

            var document = MapEntityToDocument(entity);
            var response = await openSearchClient.UpdateAsync<NovelEntitySearchDocument>(
                entityId.ToString(),
                u => u
                    .Index(_indexName)
                    .Doc(document)
                    .DocAsUpsert(true)
            );

            if (!response.IsValid)
            {
                logger.LogError("Failed to update entity {EntityId}: {Error}", entityId, response.DebugInformation);
                return false;
            }

            logger.LogInformation("Successfully updated entity {EntityId} in index", entityId);
            return true;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error updating entity {EntityId} in index", entityId);
            return false;
        }
    }

    public async Task<bool> DeleteEntityFromIndexAsync(Guid entityId)
    {
        try
        {
            var response = await openSearchClient.DeleteAsync<NovelEntitySearchDocument>(
                entityId.ToString(),
                d => d.Index(_indexName)
            );

            if (!response.IsValid && response.Result != Result.NotFound)
            {
                logger.LogError("Failed to delete entity {EntityId}: {Error}", entityId, response.DebugInformation);
                return false;
            }

            logger.LogInformation("Successfully deleted entity {EntityId} from index", entityId);
            return true;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error deleting entity {EntityId} from index", entityId);
            return false;
        }
    }

    public async Task<bool> BulkIndexEntitiesAsync(IEnumerable<Guid> entityIds)
    {
        try
        {
            var entityIdsList = entityIds.ToList();
            logger.LogInformation("Starting bulk indexing of {Count} entities", entityIdsList.Count);

            var documents = new List<NovelEntitySearchDocument>();

            foreach (var entityId in entityIdsList)
            {
                var entity = await entityRepository.GetEntityByIdAsync(entityId);
                if (entity != null && !entity.IsDeleted)
                {
                    documents.Add(MapEntityToDocument(entity));
                }
            }

            if (documents.Count == 0)
            {
                logger.LogInformation("No eligible entities to index");
                return true;
            }

            var bulkResponse = await openSearchClient.BulkAsync(b => b
                .Index(_indexName)
                .IndexMany(documents)
            );

            if (!bulkResponse.IsValid)
            {
                logger.LogError("Bulk indexing failed: {Error}", bulkResponse.DebugInformation);
                return false;
            }

            logger.LogInformation("Successfully bulk indexed {Count} entities", documents.Count);
            return true;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error during bulk indexing");
            return false;
        }
    }

    public async Task<int> ReindexNovelEntitiesAsync(Guid novelId)
    {
        try
        {
            logger.LogInformation("Reindexing all entities for novel {NovelId}", novelId);

            var (entities, totalCount) = await entityRepository.GetNovelEntitiesAsync(novelId, pageSize: int.MaxValue);
            var eligibleEntities = entities.Where(e => !e.IsDeleted).ToList();

            logger.LogInformation(
                "Found {Total} total entities, {Eligible} eligible for indexing",
                totalCount,
                eligibleEntities.Count
            );

            var documents = eligibleEntities.Select(MapEntityToDocument).ToList();

            if (documents.Count == 0)
            {
                return 0;
            }

            var bulkResponse = await openSearchClient.BulkAsync(b => b
                .Index(_indexName)
                .IndexMany(documents)
            );

            if (!bulkResponse.IsValid)
            {
                logger.LogError("Reindexing failed: {Error}", bulkResponse.DebugInformation);
                return 0;
            }

            logger.LogInformation("Reindexed {Count} entities for novel {NovelId}", documents.Count, novelId);
            return documents.Count;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error reindexing entities for novel {NovelId}", novelId);
            return 0;
        }
    }

    public async Task<bool> EnsureIndexExistsAsync()
    {
        try
        {
            var existsResponse = await openSearchClient.Indices.ExistsAsync(_indexName);
            if (existsResponse.Exists)
            {
                logger.LogInformation("Index {IndexName} already exists", _indexName);
                return true;
            }

            logger.LogInformation("Creating index {IndexName}", _indexName);

            var createResponse = await openSearchClient.Indices.CreateAsync(_indexName, c => c
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
                .Map<NovelEntitySearchDocument>(m => m
                    .Properties(p => p
                        .Keyword(k => k.Name(n => n.Id))
                        .Keyword(k => k.Name(n => n.NovelId))
                        .Keyword(k => k.Name(n => n.Section))
                        .Keyword(k => k.Name(n => n.Icon))
                        .Text(t => t
                            .Name(n => n.Name)
                            .Analyzer("arabic_custom")
                        )
                        .Text(t => t.Name(n => n.ShortDescription))
                        .Text(t => t.Name(n => n.Description))
                        .Text(t => t.Name(n => n.Role))
                        .Keyword(k => k.Name(n => n.ImageUrl))
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

    public async Task<Dictionary<string, int>> GetEntityTypeCountsAsync(Guid novelId)
    {
        try
        {
            var response = await openSearchClient.SearchAsync<NovelEntitySearchDocument>(s => s
                .Index(_indexName)
                .Size(0)
                .Query(q => q.Term(t => t.Field(f => f.NovelId).Value(novelId.ToString())))
                .Aggregations(a => a
                    .Terms("sections", t => t.Field(f => f.Section.Suffix("keyword")))
                )
            );

            if (!response.IsValid)
            {
                logger.LogError("Failed to get section counts: {Error}", response.DebugInformation);
                return new Dictionary<string, int>();
            }

            var buckets = response.Aggregations.Terms("sections").Buckets;
            return buckets.ToDictionary(b => b.Key, b => (int)b.DocCount.GetValueOrDefault());
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error getting section counts");
            return new Dictionary<string, int>();
        }
    }

    public async Task<List<string>> GetAttributeKeysAsync(Guid novelId, string section)
    {
        try
        {
            var response = await openSearchClient.SearchAsync<NovelEntitySearchDocument>(s => s
                .Index(_indexName)
                .Size(100)
                .Query(q => q.Bool(b => b
                    .Must(
                        m => m.Term(t => t.Field(f => f.NovelId).Value(novelId.ToString())),
                        m => m.Term(t => t.Field(f => f.Section).Value(section))
                    )
                ))
            );

            if (!response.IsValid || !response.Documents.Any())
            {
                return new List<string>();
            }

            return new List<string>();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error getting attribute keys");
            return new List<string>();
        }
    }

    private QueryContainer BuildSearchQuery(
        QueryContainerDescriptor<NovelEntitySearchDocument> q,
        Guid novelId,
        string? query,
        string? section)
    {
        var mustClauses = new List<QueryContainer>
        {
            q.Term(t => t.Field(f => f.NovelId).Value(novelId.ToString()))
        };

        if (!string.IsNullOrEmpty(section))
        {
            mustClauses.Add(q.Term(t => t.Field(f => f.Section).Value(section)));
        }

        if (!string.IsNullOrEmpty(query))
        {
            mustClauses.Add(q.MultiMatch(m => m
                .Query(query)
                .Fields(f => f
                    .Field(e => e.Name, boost: 2)
                    .Field(e => e.Description)
                    .Field(e => e.ShortDescription)
                )
                .Fuzziness(Fuzziness.Auto)
            ));
        }

        return q.Bool(b => b.Must(mustClauses.ToArray()));
    }

    private NovelEntitySearchDocument MapEntityToDocument(Domain.Entities.NovelEntity entity)
    {
        var attributes = new Dictionary<string, object>();
        
        try
        {
            if (!string.IsNullOrEmpty(entity.AttributesJson) && entity.AttributesJson != "{}")
            {
                attributes = JsonSerializer.Deserialize<Dictionary<string, object>>(entity.AttributesJson) 
                    ?? new Dictionary<string, object>();
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to parse attributes JSON for entity {EntityId}", entity.Id);
        }

        return new NovelEntitySearchDocument
        {
            Id = entity.Id.ToString(),
            NovelId = entity.NovelId.ToString(),
            Section = entity.Section,
            Icon = entity.Icon,
            Name = entity.Name,
            ShortDescription = entity.ShortDescription,
            Description = entity.Description,
            Role = entity.Role,
            ImageUrl = entity.ImageUrl,
        };
    }
}
