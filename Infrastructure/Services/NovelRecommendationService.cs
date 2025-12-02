using Application.Novels.DTOS;
using Application.Search.DTOs;
using Application.Services;
using Domain.Repositories;
using Microsoft.Extensions.Caching.Memory;
using OpenSearch.Client;

namespace Infrastructure.Services;

public class NovelRecommendationService(
    IOpenSearchClient openSearchClient,
    IMemoryCache cache,
    INovelsRepository novelsRepository) : INovelRecommendationService
{
    private const string CACHE_VERSION = "v1";
    private const int CACHE_HOURS = 24;

    public async Task<List<NovelRecommendationDto>> GetRecommendationsAsync(Guid novelId, int count = 10)
    {
        var cacheKey = $"recommendations:{novelId}:{CACHE_VERSION}";

        if (cache.TryGetValue(cacheKey, out List<NovelRecommendationDto>? cached) && cached != null)
        {
            return cached.Take(count).ToList();
        }

        var recommendations = await GetRecommendationsFromOpenSearchAsync(novelId, count * 2);

        var cacheOptions = new MemoryCacheEntryOptions()
            .SetAbsoluteExpiration(TimeSpan.FromHours(CACHE_HOURS))
            .SetSize(1);

        cache.Set(cacheKey, recommendations, cacheOptions);

        return recommendations.Take(count).ToList();
    }

    public async Task InvalidateRecommendationCacheAsync(Guid novelId)
    {
        var cacheKey = $"recommendations:{novelId}:{CACHE_VERSION}";
        cache.Remove(cacheKey);
    }

    private async Task<List<NovelRecommendationDto>> GetRecommendationsFromOpenSearchAsync(
        Guid novelId,
        int maxResults)
    {
        var sourceCheck = await openSearchClient.GetAsync<NovelSearchDocument>(novelId.ToString(), 
            g => g.Index("sareed-novels"));
        
        if (!sourceCheck.Found)
        {
            return new List<NovelRecommendationDto>();
        }

        var summaryLength = sourceCheck.Source.Summary?.Length ?? 0;
        var genreCount = sourceCheck.Source.Genres?.Count ?? 0;
        
        if (summaryLength < 20 && genreCount == 0)
        {
            return new List<NovelRecommendationDto>();
        }

        var response = await openSearchClient.SearchAsync<NovelSearchDocument>(s => s
            .Index("sareed-novels")
            .Size(maxResults)
            .Query(q => q
                .Bool(b => b
                    .Should(
                        sh => sh.MoreLikeThis(mlt => mlt
                            .Fields(f => f.Field(n => n.Summary))
                            .MinTermFrequency(1)
                            .MaxQueryTerms(50)
                            .MinimumShouldMatch(1)
                            .MinWordLength(2)
                            .Like(l => l.Text(sourceCheck.Source.Summary))
                            .Boost(2.0)
                        ),
                        sh => sh.Terms(t => t
                            .Field(f => f.Genres)
                            .Terms(sourceCheck.Source.Genres ?? new List<string>())
                            .Boost(1.5)
                        )
                    )
                    .MustNot(
                        mn => mn.Term(t => t.Field(f => f.Id).Value(novelId))
                    )
                    .Filter(
                        fi => fi.Term(t => t.Field("isEligibleForRanking").Value(true)),
                        fi => fi.Term(t => t.Field("isDraft").Value(false))
                    )
                    .MinimumShouldMatch(1)
                )
            )
        );

        if (!response.IsValid || !response.Documents.Any())
        {
            return new List<NovelRecommendationDto>();
        }

        var novelIds = response.Documents
            .Select(d => d.Id)
            .ToList();

        var novels = await novelsRepository.GetNovelsByIdsAsync(novelIds);

        var recommendations = novels
            .Select(novel => new NovelRecommendationDto
            {
                Id = novel.Id,
                Title = novel.Title,
                Slug = novel.Slug,
                CoverImageUrl = novel.CoverImageUrl,
                Summary = novel.Summary,
                Status = novel.Status,
                TotalViews = novel.TotalViews,
                ChapterCount = novel.ChapterCount,
                SimilarityScore = GetOpenSearchScore(response, novel.Id)
            })
            .OrderByDescending(r => r.SimilarityScore)
            .ToList();

        return recommendations;
    }

    private decimal GetOpenSearchScore(ISearchResponse<NovelSearchDocument> response, Guid novelId)
    {
        var hit = response.Hits.FirstOrDefault(h => h.Source.Id == novelId);
        return hit?.Score.HasValue == true ? (decimal)hit.Score.Value : 0;
    }
}

