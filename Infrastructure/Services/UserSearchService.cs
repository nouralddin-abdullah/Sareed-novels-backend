using Application.Common;
using Application.Search.DTOs;
using Application.Services;
using Domain.Repositories;
using Infrastructure.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Nest;

namespace Infrastructure.Services;

public class UserSearchService(
    IElasticClient elasticClient,
    IUsersRepository usersRepository,
    ILogger<UserSearchService> logger,
    IOptions<ElasticsearchSettings> settings) : IUserSearchService
{
    private readonly string _indexName = "sareed-users";

    public async Task<PagedResult<UserSearchResult>> SearchUsersAsync(
        SearchUsersRequest request,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var searchDescriptor = new SearchDescriptor<UserSearchDocument>()
                .Index(_indexName)
                .From((request.PageNumber - 1) * request.PageSize)
                .Size(request.PageSize)
                .Query(q => BuildSearchQuery(q, request))
                .Sort(s => s.Descending(SortSpecialField.Score)) // Sort by relevance/match score
                .TrackTotalHits(true);

            var response = await elasticClient.SearchAsync<UserSearchDocument>(searchDescriptor, cancellationToken);

            if (!response.IsValid)
            {
                logger.LogError("User search failed: {Error}", response.DebugInformation);
                return new PagedResult<UserSearchResult>(
                    new List<UserSearchResult>(),
                    0,
                    request.PageSize,
                    request.PageNumber
                );
            }

            var results = response.Documents.Select(doc => new UserSearchResult
            {
                Id = doc.Id,
                UserName = doc.UserName,
                DisplayName = doc.DisplayName,
                ProfilePhoto = doc.ProfilePhoto,
                FollowersCount = doc.FollowersCount,
                FollowingCount = doc.FollowingCount,
                NovelsCount = doc.NovelsCount
            }).ToList();

            var totalCount = response.Total;

            logger.LogInformation(
                "User search completed: query={Query}, total={Total}, page={Page}",
                request.Query ?? "all",
                totalCount,
                request.PageNumber
            );

            return new PagedResult<UserSearchResult>(
                results,
                (int)totalCount,
                request.PageSize,
                request.PageNumber
            );
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error performing user search");
            return new PagedResult<UserSearchResult>(
                new List<UserSearchResult>(),
                0,
                request.PageSize,
                request.PageNumber
            );
        }
    }

    public async Task<bool> IndexUserAsync(string userId)
    {
        try
        {
            var user = await usersRepository.GetUserById(userId);
            if (user == null)
            {
                logger.LogWarning("User {UserId} not found for indexing", userId);
                return false;
            }

            var document = await MapUserToDocument(user);
            var response = await elasticClient.IndexAsync(document, idx => idx.Index(_indexName));

            if (!response.IsValid)
            {
                logger.LogError("Failed to index user {UserId}: {Error}", userId, response.DebugInformation);
                return false;
            }

            logger.LogInformation("Successfully indexed user {UserId}", userId);
            return true;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error indexing user {UserId}", userId);
            return false;
        }
    }

    public async Task<bool> UpdateUserInIndexAsync(string userId)
    {
        try
        {
            var user = await usersRepository.GetUserById(userId);
            if (user == null)
            {
                logger.LogWarning("User {UserId} not found for updating", userId);
                return await DeleteUserFromIndexAsync(userId);
            }

            var document = await MapUserToDocument(user);
            var response = await elasticClient.UpdateAsync<UserSearchDocument>(
                userId,
                u => u
                    .Index(_indexName)
                    .Doc(document)
                    .DocAsUpsert(true)
            );

            if (!response.IsValid)
            {
                logger.LogError("Failed to update user {UserId}: {Error}", userId, response.DebugInformation);
                return false;
            }

            logger.LogInformation("Successfully updated user {UserId} in index", userId);
            return true;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error updating user {UserId} in index", userId);
            return false;
        }
    }

    public async Task<bool> DeleteUserFromIndexAsync(string userId)
    {
        try
        {
            var response = await elasticClient.DeleteAsync<UserSearchDocument>(
                userId,
                d => d.Index(_indexName)
            );

            if (!response.IsValid && response.Result != Result.NotFound)
            {
                logger.LogError("Failed to delete user {UserId}: {Error}", userId, response.DebugInformation);
                return false;
            }

            logger.LogInformation("Successfully deleted user {UserId} from index", userId);
            return true;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error deleting user {UserId} from index", userId);
            return false;
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
                            .Custom("username_analyzer", ca => ca
                                .Tokenizer("keyword")
                                .Filters("lowercase")
                            )
                        )
                        .TokenFilters(tf => tf
                            .EdgeNGram("username_ngram", ng => ng
                                .MinGram(2)
                                .MaxGram(20)
                            )
                        )
                    )
                )
                .Map<UserSearchDocument>(m => m
                    .Properties(p => p
                        .Keyword(k => k.Name(n => n.Id))
                        .Text(t => t
                            .Name(n => n.UserName)
                            .Analyzer("username_analyzer")
                            .SearchAnalyzer("keyword")
                            .Fields(f => f
                                .Keyword(k => k.Name("keyword"))
                                .Text(tt => tt
                                    .Name("ngram")
                                    .Analyzer("username_analyzer")
                                    .SearchAnalyzer("keyword")
                                    .Fields(fff => fff
                                        .Text(t => t
                                            .Name("edge")
                                            .Analyzer("username_analyzer")
                                            .SearchAnalyzer("keyword")
                                        )
                                    )
                                )
                            )
                        )
                        .Text(t => t
                            .Name(n => n.DisplayName)
                            .Analyzer("arabic_custom")
                        )
                        .Keyword(k => k.Name(n => n.ProfilePhoto))
                        .Number(n => n.Name(nn => nn.FollowersCount).Type(NumberType.Integer))
                        .Number(n => n.Name(nn => nn.FollowingCount).Type(NumberType.Integer))
                        .Number(n => n.Name(nn => nn.NovelsCount).Type(NumberType.Integer))
                        .Date(d => d.Name(n => n.CreatedAt))
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

    public async Task<int> ReindexAllUsersAsync()
    {
        try
        {
            logger.LogInformation("Starting full reindex of all users");

            var users = await usersRepository.GetAllUsers();
            logger.LogInformation("Found {Total} users for indexing", users.Count());

            var documents = new List<UserSearchDocument>();
            foreach (var user in users)
            {
                documents.Add(await MapUserToDocument(user));
            }

            if (documents.Count == 0)
            {
                logger.LogInformation("No users to index");
                return 0;
            }

            var bulkResponse = await elasticClient.BulkAsync(b => b
                .Index(_indexName)
                .IndexMany(documents)
            );

            if (!bulkResponse.IsValid)
            {
                logger.LogError("Bulk user indexing failed: {Error}", bulkResponse.DebugInformation);
                return 0;
            }

            logger.LogInformation("Reindexing completed: {Count} users indexed", documents.Count);
            return documents.Count;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error during full reindexing");
            return 0;
        }
    }

    private QueryContainer BuildSearchQuery(QueryContainerDescriptor<UserSearchDocument> q, SearchUsersRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Query))
        {
            return q.MatchAll();
        }

        var searchTerm = request.Query.ToLower();

        return q.Bool(b => b
            .Should(
                // Exact username match (highest priority)
                s => s.Term(t => t
                    .Field(f => f.UserName.Suffix("keyword"))
                    .Value(searchTerm)
                    .Boost(5.0)
                ),
                // Username prefix match (e.g., "hmo" matches "Hmot_mbdon")
                s => s.Prefix(p => p
                    .Field(f => f.UserName)
                    .Value(searchTerm)
                    .Boost(3.0)
                ),
                // Username contains (wildcard)
                s => s.Wildcard(w => w
                    .Field(f => f.UserName)
                    .Value($"*{searchTerm}*")
                    .Boost(2.0)
                ),
                // Display name fuzzy match
                s => s.Match(m => m
                    .Field(f => f.DisplayName)
                    .Query(request.Query)
                    .Fuzziness(Fuzziness.Auto)
                    .Boost(1.5)
                )
            )
            .MinimumShouldMatch(1)
        );
    }

    private async Task<UserSearchDocument> MapUserToDocument(Domain.Entities.User user)
    {
        var followersCount = await usersRepository.GetFollowersCount(user.Id);
        var followingCount = await usersRepository.GetFollowingCount(user.Id);
        var novelsCount = await usersRepository.GetNovelsCount(user.Id);

        return new UserSearchDocument
        {
            Id = user.Id,
            UserName = user.UserName ?? string.Empty,
            DisplayName = user.DisplayName ?? user.UserName ?? string.Empty,
            ProfilePhoto = user.ProfilePhoto,
            FollowersCount = followersCount,
            FollowingCount = followingCount,
            NovelsCount = novelsCount,
            CreatedAt = user.CreatedAt
        };
    }
}
