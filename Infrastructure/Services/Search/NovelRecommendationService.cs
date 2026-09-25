using Application.Novels.DTOS;
using Application.Services;
using Domain.Exceptions;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace Infrastructure.Services.Search;

/// <summary>
/// "Readers of this novel also read": co-readership from reading progress, blended with genre overlap, falling back
/// to the most-read novels so the sidebar is never empty. Dividing by sqrt(readers) keeps the biggest novels from
/// being recommended everywhere just for being big. An author's reading of their own novel is not readership, and
/// no author gets more than <see cref="MaxPerAuthor"/> places ahead of other authors' novels.
/// </summary>
public class NovelRecommendationService(ApplicationDbContext dbContext, IMemoryCache cache) : INovelRecommendationService
{
    private const string Published = "Published";
    private const int MaxCount = 20;
    public const int MaxPerAuthor = 2;
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

    /// <summary>
    /// Keeps the ranking order but lets each author take at most <paramref name="maxPerAuthor"/> places before every
    /// other author has had a turn; the rest of that author's novels follow, still in order, so nothing is dropped.
    /// </summary>
    public static List<T> LimitPerAuthor<T>(IEnumerable<T> ranked, Func<T, string> authorOf, int maxPerAuthor)
    {
        var placed = new Dictionary<string, int>();
        var first = new List<T>();
        var deferred = new List<T>();
        foreach (var item in ranked)
        {
            var author = authorOf(item);
            var count = placed.GetValueOrDefault(author);
            if (count < maxPerAuthor)
            {
                first.Add(item);
                placed[author] = count + 1;
            }
            else
            {
                deferred.Add(item);
            }
        }

        first.AddRange(deferred);
        return first;
    }

    private async Task<List<NovelRecommendationDto>> ComputeAsync(Guid novelId)
    {
        // A missing (or deleted) novel is a 404, not an empty list, and must not be cached as one.
        var source = await dbContext.Novels.AsNoTracking()
            .Where(n => n.Id == novelId)
            .Select(n => new { n.AuthorId, GenreIds = n.NovelGenres.Select(g => g.GenreId).ToList() })
            .FirstOrDefaultAsync()
            ?? throw new NotFoundException("This novel was not found");

        var readerIds = dbContext.UserNovelProgress
            .Where(p => p.NovelId == novelId && p.UserId != source.AuthorId)
            .Select(p => p.UserId);

        // An author previewing their own chapters is not a reader of that novel, so it must not tie their novels
        // to everything else they read.
        var sharedReaders = await dbContext.UserNovelProgress
            .Where(p => p.NovelId != novelId && readerIds.Contains(p.UserId) && p.UserId != p.Novel.AuthorId)
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
                n.AuthorId,
                n.Title,
                n.Slug,
                n.CoverImageUrl,
                n.Summary,
                n.Status,
                n.TotalViews,
                ChapterCount = n.Chapters.Count(c => c.Status == Published),
                SharedGenres = n.NovelGenres.Count(g => genreIds.Contains(g.GenreId)),
                Readers = dbContext.UserNovelProgress.Count(p => p.NovelId == n.Id && p.UserId != n.AuthorId)
            })
            .ToListAsync();

        var ranked = candidates
            .Select(c =>
            {
                var shared = sharedReaders.GetValueOrDefault(c.Id);
                var score = 3.0 * shared / Math.Sqrt(c.Readers + 1) + c.SharedGenres + 0.1 * Math.Log(1 + c.Readers);
                return (c.AuthorId, Dto: new NovelRecommendationDto
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
                });
            })
            .OrderByDescending(r => r.Dto.SimilarityScore)
            .ThenBy(r => r.Dto.Id);

        return LimitPerAuthor(ranked, r => r.AuthorId, MaxPerAuthor)
            .Take(MaxCount)
            .Select(r => r.Dto)
            .ToList();
    }

    private static string CacheKey(Guid novelId) => $"recommendations:v2:{novelId}";
}
