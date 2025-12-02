# Novel Recommendations with OpenSearch - Implementation Plan

## 🎯 Architecture Overview

```
User requests recommendations for Novel A
  ↓
Check cache: "recommendations:{novelId}"
  ↓
┌─────────────┐
│ Cache Hit?  │
└─────┬───────┘
      │
   Yes│         No
      │          ↓
      │    ┌──────────────────┐
      │    │ OpenSearch Query │
      │    │ "More Like This" │
      │    └────────┬─────────┘
      │             │
      │             ↓
      │    ┌──────────────────┐
      │    │ Enrich with      │
      │    │ genres, ratings  │
      │    └────────┬─────────┘
      │             │
      │             ↓
      │    ┌──────────────────┐
      │    │ Cache for 24h    │
      │    └────────┬─────────┘
      │             │
      ↓             ↓
Return recommendations (1-2ms vs 50-100ms first time)
```

---

## 🔧 OpenSearch "More Like This" Configuration

### 1. **Balance: Accuracy vs Coverage**

```csharp
var response = await openSearchClient.SearchAsync<NovelSearchDocument>(s => s
    .Index("sareed-novels")
    .Size(20) // Get more candidates than needed
    .Query(q => q
        .MoreLikeThis(mlt => mlt
            // ✅ Fields to analyze for similarity
            .Fields(f => f
                .Field(n => n.Summary, boost: 2.0)    // Most important
                .Field(n => n.Genres)                  // Genre matching
                .Field(n => n.Title, boost: 0.5)       // Less important
            )
            // ✅ WIDE enough to get suggestions
            .MinTermFrequency(1)        // Low = more relaxed (default 2)
            .MaxQueryTerms(25)          // High = more terms considered (default 12)
            .MinDocFrequency(1)         // Low = rare terms matter
            .MinWordLength(3)           // Ignore short words
            
            // ✅ Similarity source
            .Like(l => l.Document(d => d.Id(sourceNovelId.ToString())))
            
            // ✅ Boost recent & quality novels
            .Boost(1.0)
        )
    )
    .PostFilter(pf => pf.Bool(b => b
        .Must(
            m => m.Term(t => t.Field("isEligibleForRanking").Value(true)),
            m => m.Term(t => t.Field("isDraft").Value(false)),
            m => m.Range(r => r.Field("reviewCount").GreaterThanOrEquals(1)) // At least 1 review
        )
        .MustNot(
            mn => mn.Term(t => t.Field("id").Value(sourceNovelId.ToString())) // Exclude source
        )
    ))
);
```

---

## 📊 Recommendation Tuning Guide

### Problem: **Too Few Recommendations**

```csharp
// ❌ TOO STRICT (might get 0-2 results)
.MinTermFrequency(3)      // Requires 3+ occurrences
.MaxQueryTerms(5)         // Only uses 5 terms
.MinDocFrequency(5)       // Term must appear in 5+ docs
```

### ✅ Solution: **Relax Parameters**

```csharp
// ✅ BALANCED (gets 8-15 results typically)
.MinTermFrequency(1)      // Accept terms appearing once
.MaxQueryTerms(25)        // Use up to 25 terms
.MinDocFrequency(1)       // Accept rare terms
```

### Problem: **Too Many Irrelevant Recommendations**

```csharp
// ❌ TOO LOOSE (gets 50+ random novels)
.MinTermFrequency(1)
.MaxQueryTerms(50)
.MinSimilarity(0.3)       // Very low similarity threshold
```

### ✅ Solution: **Add Scoring & Filters**

```csharp
// ✅ POST-FILTER for quality
.PostFilter(pf => pf.Bool(b => b
    .Should(
        // Prefer novels with good ratings
        s => s.Range(r => r.Field("totalAverageScore").GreaterThanOrEquals(3.5)),
        // Prefer popular novels
        s => s.Range(r => r.Field("totalViews").GreaterThanOrEquals(100))
    )
    .MinimumShouldMatch(1)
))
```

---

## 🗄️ Caching Strategy

### 1. **Cache Key Design**

```csharp
var cacheKey = $"recommendations:{novelId}:v2"; // v2 = cache version
```

**Why version number?**
- Change algorithm → Change version → Invalidate all caches
- `v2` → `v3` when you update OpenSearch parameters

### 2. **Cache Implementation**

```csharp
public class NovelRecommendationService : INovelRecommendationService
{
    private readonly IOpenSearchClient _openSearchClient;
    private readonly IMemoryCache _cache;
    private readonly INovelsRepository _novelsRepository;
    private readonly ILogger<NovelRecommendationService> _logger;
    
    private const string CACHE_VERSION = "v2";
    private const int CACHE_HOURS = 24;
    
    public async Task<List<NovelRecommendationDto>> GetRecommendationsAsync(
        Guid novelId, 
        int count = 10)
    {
        var cacheKey = $"recommendations:{novelId}:{CACHE_VERSION}";
        
        // ✅ Step 1: Try cache
        if (_cache.TryGetValue(cacheKey, out List<NovelRecommendationDto> cached))
        {
            _logger.LogDebug("Cache hit for novel {NovelId}", novelId);
            return cached.Take(count).ToList();
        }
        
        // ✅ Step 2: Query OpenSearch
        _logger.LogInformation("Cache miss for novel {NovelId}, querying OpenSearch", novelId);
        
        var stopwatch = Stopwatch.StartNew();
        
        var recommendations = await GetRecommendationsFromOpenSearchAsync(novelId, count * 2);
        
        stopwatch.Stop();
        _logger.LogInformation(
            "OpenSearch returned {Count} recommendations for novel {NovelId} in {Ms}ms",
            recommendations.Count, novelId, stopwatch.ElapsedMilliseconds);
        
        // ✅ Step 3: Cache for 24 hours
        var cacheOptions = new MemoryCacheEntryOptions()
            .SetAbsoluteExpiration(TimeSpan.FromHours(CACHE_HOURS))
            .SetSize(1); // For memory management
        
        _cache.Set(cacheKey, recommendations, cacheOptions);
        
        return recommendations.Take(count).ToList();
    }
    
    private async Task<List<NovelRecommendationDto>> GetRecommendationsFromOpenSearchAsync(
        Guid novelId, 
        int maxResults)
    {
        try
        {
            // Step 1: OpenSearch "More Like This" query
            var response = await _openSearchClient.SearchAsync<NovelSearchDocument>(s => s
                .Index("sareed-novels")
                .Size(maxResults)
                .Query(q => q
                    .MoreLikeThis(mlt => mlt
                        .Fields(f => f
                            .Field(n => n.Summary, boost: 2.0)
                            .Field(n => n.Genres)
                        )
                        .MinTermFrequency(1)
                        .MaxQueryTerms(25)
                        .MinDocFrequency(1)
                        .MinWordLength(3)
                        .Like(l => l.Document(d => d.Id(novelId.ToString())))
                    )
                )
                .PostFilter(pf => pf.Bool(b => b
                    .Must(
                        m => m.Term(t => t.Field("isEligibleForRanking").Value(true)),
                        m => m.Term(t => t.Field("isDraft").Value(false))
                    )
                    .MustNot(
                        mn => mn.Term(t => t.Field("id").Value(novelId.ToString()))
                    )
                ))
            );
            
            if (!response.IsValid || !response.Documents.Any())
            {
                _logger.LogWarning(
                    "OpenSearch returned no results for novel {NovelId}: {Error}",
                    novelId, response.DebugInformation);
                return new List<NovelRecommendationDto>();
            }
            
            // Step 2: Get novel IDs from OpenSearch
            var novelIds = response.Documents
                .Select(d => Guid.Parse(d.Id))
                .ToList();
            
            // Step 3: Enrich with database data (genres, full details)
            var novels = await _novelsRepository.GetNovelsByIdsAsync(novelIds);
            
            // Step 4: Map to DTOs with OpenSearch scores
            var recommendations = novels
                .Select(novel => new NovelRecommendationDto
                {
                    Id = novel.Id,
                    Title = novel.Title,
                    Slug = novel.Slug,
                    CoverImageUrl = novel.CoverImageUrl,
                    Summary = novel.Summary,
                    Genres = novel.NovelGenres
                        .Select(ng => new GenreSmallDto
                        {
                            Id = ng.Genre.Id,
                            Name = ng.Genre.Name,
                            Slug = ng.Genre.Slug
                        })
                        .ToList(),
                    TotalAverageScore = novel.TotalAverageScore,
                    TotalViews = novel.TotalViews,
                    ReviewCount = novel.ReviewCount,
                    SimilarityScore = GetOpenSearchScore(response, novel.Id)
                })
                .Where(r => r.ReviewCount >= 1) // Quality filter
                .OrderByDescending(r => r.SimilarityScore)
                .ToList();
            
            return recommendations;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get recommendations from OpenSearch for novel {NovelId}", novelId);
            return new List<NovelRecommendationDto>();
        }
    }
    
    private decimal GetOpenSearchScore(
        ISearchResponse<NovelSearchDocument> response, 
        Guid novelId)
    {
        var hit = response.Hits.FirstOrDefault(h => h.Source.Id == novelId.ToString());
        return hit?.Score.HasValue == true ? (decimal)hit.Score.Value : 0;
    }
}
```

---

## 🔄 Cache Invalidation Strategy

### When to Invalidate?

```csharp
// 1. Novel Updated (summary, genres changed)
public async Task OnNovelUpdatedAsync(Guid novelId)
{
    var cacheKey = $"recommendations:{novelId}:{CACHE_VERSION}";
    _cache.Remove(cacheKey);
    
    _logger.LogInformation("Invalidated recommendation cache for novel {NovelId}", novelId);
}

// 2. Novel Deleted
public async Task OnNovelDeletedAsync(Guid novelId)
{
    // Remove its own cache
    _cache.Remove($"recommendations:{novelId}:{CACHE_VERSION}");
    
    // Problem: Can't easily remove all caches that reference this novel
    // Solution: Natural expiration (24 hours) is acceptable
}

// 3. Daily cleanup (optional)
public async Task CleanupExpiredCachesAsync()
{
    // Memory cache handles this automatically with expiration
    // No action needed
}

// 4. Manual invalidation (for testing/debugging)
public async Task InvalidateAllRecommendationCachesAsync()
{
    // Change CACHE_VERSION from "v2" to "v3"
    // All old caches become orphaned
}
```

---

## 📐 Fallback Strategy

### What if OpenSearch is down?

```csharp
public async Task<List<NovelRecommendationDto>> GetRecommendationsAsync(
    Guid novelId, 
    int count = 10)
{
    var cacheKey = $"recommendations:{novelId}:{CACHE_VERSION}";
    
    // Try cache
    if (_cache.TryGetValue(cacheKey, out List<NovelRecommendationDto> cached))
    {
        return cached.Take(count).ToList();
    }
    
    try
    {
        // Try OpenSearch
        return await GetRecommendationsFromOpenSearchAsync(novelId, count);
    }
    catch (Exception ex)
    {
        _logger.LogError(ex, "OpenSearch failed, falling back to SQL");
        
        // ✅ FALLBACK: Use SQL-based genre matching
        return await GetRecommendationsFromSQLAsync(novelId, count);
    }
}

private async Task<List<NovelRecommendationDto>> GetRecommendationsFromSQLAsync(
    Guid novelId, 
    int count)
{
    // Simple genre-based recommendations (from previous analysis)
    var sourceGenres = await _novelsRepository.GetNovelGenreIdsAsync(novelId);
    
    var recommendations = await _novelsRepository.GetNovelsByGenresAsync(
        sourceGenres, 
        excludeNovelId: novelId,
        limit: count);
    
    return recommendations;
}
```

---

## 🎯 Tuning Parameters Guide

### **Scenario 1: Arabic Novels with Limited Content**

**Problem:** Few novels, short summaries
**Solution:**
```csharp
.MinTermFrequency(1)        // Very relaxed
.MaxQueryTerms(30)          // Use many terms
.MinDocFrequency(1)         // Accept rare terms
.MinWordLength(2)           // Arabic words can be short
```

### **Scenario 2: Large Database (10,000+ novels)**

**Problem:** Too many results
**Solution:**
```csharp
.MinTermFrequency(2)        // Slightly stricter
.MaxQueryTerms(20)          // Balanced
.MinDocFrequency(2)         // Common terms preferred
.MinSimilarity(0.6)         // Higher threshold
```

### **Scenario 3: Quality Over Quantity**

**Problem:** Want only the best matches
**Solution:**
```csharp
.MinTermFrequency(2)
.MaxQueryTerms(15)
.MinSimilarity(0.7)         // High threshold
.PostFilter(pf => pf.Bool(b => b
    .Must(
        m => m.Range(r => r.Field("totalAverageScore").GreaterThanOrEquals(4.0)),
        m => m.Range(r => r.Field("reviewCount").GreaterThanOrEquals(5))
    )
))
```

---

## 🗄️ Repository Method

```csharp
// Add to INovelsRepository
Task<List<Novel>> GetNovelsByIdsAsync(List<Guid> novelIds);

// Implementation in NovelsRepository
public async Task<List<Novel>> GetNovelsByIdsAsync(List<Guid> novelIds)
{
    if (novelIds == null || novelIds.Count == 0)
        return new List<Novel>();
    
    return await dbContext.Novels
        .AsNoTracking()
        .Where(n => novelIds.Contains(n.Id))
        .Include(n => n.NovelGenres)
            .ThenInclude(ng => ng.Genre)
        .Include(n => n.Owner)
        .ToListAsync();
}
```

---

## 📊 Performance Metrics

### Expected Performance

| Scenario | Response Time | Database Queries | OpenSearch Queries |
|----------|---------------|------------------|--------------------|
| **Cache Hit** | 1-2ms | 0 | 0 |
| **Cache Miss** | 50-100ms | 1 (enrich) | 1 (MLT) |
| **OpenSearch Down** | 80-120ms | 3 (SQL fallback) | 0 (failed) |

### Cache Hit Rate Prediction

- First 24 hours: ~40-50% (building up)
- After 48 hours: ~85-90% (steady state)
- Popular novels: ~95%+ (frequently requested)

---

## 🚀 Implementation Checklist

### Phase 1: Core Service (3-4 hours)
- [ ] Create `INovelRecommendationService` interface
- [ ] Implement caching layer with 24h expiration
- [ ] Implement OpenSearch "More Like This" query
- [ ] Add repository method `GetNovelsByIdsAsync`
- [ ] Configure tuning parameters (start with balanced)

### Phase 2: API Integration (1-2 hours)
- [ ] Create `GetNovelRecommendationsQuery`
- [ ] Create `GetNovelRecommendationsQueryHandler`
- [ ] Add controller endpoint
- [ ] Add logging & monitoring

### Phase 3: Fallback & Error Handling (2 hours)
- [ ] Implement SQL fallback for OpenSearch failures
- [ ] Add retry logic for transient OpenSearch errors
- [ ] Add circuit breaker pattern (optional)

### Phase 4: Cache Management (1 hour)
- [ ] Add cache invalidation on novel update
- [ ] Add cache version management
- [ ] Configure memory cache size limits

### Phase 5: Testing & Tuning (2-3 hours)
- [ ] Test with 10-20 sample novels
- [ ] Adjust parameters based on result quality
- [ ] Monitor OpenSearch query performance
- [ ] Verify cache hit rates

---

## 🎛️ Configuration

```csharp
// appsettings.json
{
  "NovelRecommendations": {
    "CacheVersion": "v2",
    "CacheExpirationHours": 24,
    "MaxResults": 20,
    "MinTermFrequency": 1,
    "MaxQueryTerms": 25,
    "MinDocFrequency": 1,
    "MinWordLength": 3,
    "RequireMinReviews": 1,
    "EnableSQLFallback": true
  }
}
```

---

## 💡 Pro Tips

### 1. **Monitoring**
```csharp
_logger.LogInformation(
    "Recommendations for {NovelId}: Cache={CacheHit}, OpenSearch={Ms}ms, Results={Count}",
    novelId, cacheHit, elapsed, results.Count);
```

### 2. **A/B Testing Parameters**
```csharp
// Try different configs for 1 week each
// Measure: Avg results count, Click-through rate, User engagement

// Week 1: Balanced (current)
.MinTermFrequency(1).MaxQueryTerms(25)

// Week 2: Strict
.MinTermFrequency(2).MaxQueryTerms(15)

// Week 3: Loose
.MinTermFrequency(1).MaxQueryTerms(35)
```

### 3. **Prewarming Cache**
```csharp
// Background job: Prewarm popular novels
public async Task PrewarmRecommendationCacheAsync()
{
    var popularNovels = await _novelsRepository.GetTopViewedNovelsAsync(100);
    
    foreach (var novel in popularNovels)
    {
        await GetRecommendationsAsync(novel.Id, 10);
        await Task.Delay(100); // Rate limit
    }
}
```

---

## 🎯 Expected Results

### Example: "Ultimate Spider-Man (2024)"

**Input:**
- Summary: "في هذا الكون الجديد... Peter Parker... عائل—زوج لـماري..."
- Genres: Action, Fantasy, Drama, Adventure

**OpenSearch Output (sorted by similarity):**

1. **"Black Science"** (Score: 8.7/10)
   - Why: Sci-fi, adventure, multiple universes theme
   - Shared: Fantasy, Adventure

2. **"توستي"** (Score: 6.2/10)
   - Why: Fantasy elements, character-driven
   - Shared: Fantasy

3. **"دحيح على الحديد"** (Score: 5.8/10)
   - Why: Science Fiction, protagonist journey
   - Shared: Science Fiction theme overlap

**Quality:** 8-12 recommendations typically, all relevant

---

## ✅ Final Recommendation

### Use This Approach:

**Why:**
- ✅ Best accuracy (summary + genre matching)
- ✅ Scalable (cache absorbs 90% of load)
- ✅ Resilient (SQL fallback if OpenSearch down)
- ✅ Tunable (adjust parameters easily)
- ✅ Cost-effective (cache reduces OpenSearch queries by 90%)

**Performance:**
- First request: 50-100ms
- Cached requests: 1-2ms (99% of traffic)
- OpenSearch queries: ~100/day for 1,000 daily novel views

**Cost:**
- OpenSearch: ~10 queries/hour = ~$5-10/month
- Memory cache: Free (built-in)
- SQL fallback: Free (existing infrastructure)

---

## 🚀 Next Steps

1. Implement core service with caching
2. Start with **balanced parameters** (MinTermFrequency=1, MaxQueryTerms=25)
3. Deploy and monitor cache hit rate
4. Adjust parameters based on result quality
5. Add SQL fallback for resilience

**Estimated Time:** 8-10 hours for full implementation
**Expected Performance:** 50-100ms uncached, 1-2ms cached (90%+ hit rate)
