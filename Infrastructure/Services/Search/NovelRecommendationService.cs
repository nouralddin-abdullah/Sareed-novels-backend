using Application.Novels.DTOS;
using Application.Services;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace Infrastructure.Services.Search;

/// <summary>
/// "Readers of this novel also read": co-readership from reading progress, blended with genre overlap, falling back
/// to the most-read novels so the sidebar is never empty. Dividing by sqrt(readers) keeps the biggest novels from
/// being recommended everywhere just for being big.
/// </summary>
public class NovelRecommendationService(ApplicationDbContext dbContext, IMemoryCache cache) : INovelRecommendationService
{
    private const string Published = "Published";
    private const int MaxCount = 20;
    private static readonly TimeSpan CacheDuration = TimeSpan.FromHours(6);

    public async Task<List<NovelRecommendationDto>> GetRecommendationsAsync(Guid novelId, int count = 10)
    {
        count = Math.Clamp(count, 1, MaxCount);
        var cacheKey = CacheKey(novelId);

        if (!cache.TryGetValue(cacheKey, out List<NovelRecommendationDto>? recommendations) || recommendations == null)
        {
            recommendations = await ComputeAsync(novelId);
            cache.Set(cacheKey, recommendations, new MemoryCacheEntryOptions()
                .SetAbsoluteExpiration(CacheDuration)
                .SetSize(1));
        }

        return recommendations.Take(count).ToList();
    }

    public Task InvalidateRecommendationCacheAsync(Guid novelId)
    {
        cache.Remove(CacheKey(novelId));
        return Task.CompletedTask;
    }

    private async Task<List<NovelRecommendationDto>> ComputeAsync(Guid novelId)
    {
        var source = await dbContext.Novels.AsNoTracking()
            .Where(n => n.Id == novelId)
            .Select(n => new { n.AuthorId, GenreIds = n.NovelGenres.Select(g => g.GenreId).ToList() })
            .FirstOrDefaultAsync();
        if (source == null)
        {
            return [];
        }

        var readerIds = dbContext.UserNovelProgress
            .Where(p => p.NovelId == novelId && p.UserId != source.AuthorId)
            .Select(p => p.UserId);

        var sharedReaders = await dbContext.UserNovelProgress
            .Where(p => p.NovelId != novelId && readerIds.Contains(p.UserId))
            .GroupBy(p => p.NovelId)
            .Select(g => new { NovelId = g.Key, Shared = g.Count() })
            .ToDictionaryAsync(x => x.NovelId, x => x.Shared);

        var genreIds = source.GenreIds;
        var candidates = await dbContext.Novels.AsNoTracking()
            .Where(n => n.Id != novelId && !n.IsDraft && n.IsEligibleForRanking
                        && n.Chapters.Any(c => c.Status == Published))
            .Select(n => new
            {
                n.Id,
                n.Title,
                n.Slug,
                n.CoverImageUrl,
                n.Summary,
                n.Status,
                n.TotalViews,
                ChapterCount = n.Chapters.Count(c => c.Status == Published),
                SharedGenres = n.NovelGenres.Count(g => genreIds.Contains(g.GenreId)),
                Readers = dbContext.UserNovelProgress.Count(p => p.NovelId == n.Id)
            })
            .ToListAsync();

        return candidates
            .Select(c =>
            {
                var shared = sharedReaders.GetValueOrDefault(c.Id);
                var score = 3.0 * shared / Math.Sqrt(c.Readers + 1) + c.SharedGenres + 0.1 * Math.Log(1 + c.Readers);
                return new NovelRecommendationDto
                {
                    Id = c.Id,
                    Title = c.Title,
                    Slug = c.Slug,
                    CoverImageUrl = c.CoverImageUrl,
                    Summary = c.Summary,
                    Status = c.Status,
                    TotalViews = c.TotalViews,
                    ChapterCount = c.ChapterCount,
                    SimilarityScore = (decimal)Math.Round(score, 4)
                };
            })
            .OrderByDescending(r => r.SimilarityScore)
            .ThenBy(r => r.Id)
            .Take(MaxCount)
            .ToList();
    }

    private static string CacheKey(Guid novelId) => $"recommendations:v2:{novelId}";
}
