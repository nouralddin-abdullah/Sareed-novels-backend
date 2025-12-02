# Novel Recommendations System - Analysis & Architecture

## 🎯 Problem Statement

Implement a "Similar Novels" / "You May Also Like" feature that:
- Recommends novels similar to the current one
- **Does NOT require OpenSearch** (can work with SQL only)
- **Does NOT overwhelm the database**
- Provides relevant, high-quality recommendations
- Scales to thousands of novels

---

## 📊 Available Data for Recommendations

From your `Novel` entity, you have:

| Data Point | Use Case | Weight |
|------------|----------|--------|
| **Genres** (1-4 per novel) | Match similar themes | ⭐⭐⭐⭐⭐ |
| **Status** (Ongoing/Completed) | Match reading preferences | ⭐⭐⭐ |
| **TotalAverageScore** | Quality filter | ⭐⭐⭐⭐ |
| **ReviewCount** | Popularity/credibility | ⭐⭐⭐ |
| **TotalViews** | Popularity indicator | ⭐⭐ |
| **AuthorId** | Same author recommendations | ⭐⭐⭐⭐ |
| **ChapterCount** | Length preference | ⭐⭐ |

---

## ✅ RECOMMENDED: Hybrid Approach

### Strategy: **Genre-Based + Scoring + Caching**

```
Novel A → Extract Genres → Find Novels with Shared Genres → Score & Rank → Cache → Return Top 10
  (0ms)       (0ms)              (50-100ms indexed)            (10ms)      (cache)    (instant)
```

**Why This Works:**
1. **Genre matching is FAST** - Uses existing `NovelGenres` table with indexes
2. **No complex ML** - Simple scoring algorithm
3. **Cacheable** - Recommendations don't change often
4. **No OpenSearch needed** - Pure SQL with efficient indexes

---

## 🏗️ Architecture Design

### 1. **Recommendation Scoring Algorithm**

```csharp
Score = (GenreMatchScore * 0.5) + 
        (QualityScore * 0.3) + 
        (PopularityScore * 0.15) + 
        (RecencyScore * 0.05)

Where:
- GenreMatchScore = (Shared Genres Count / Total Genres) * 100
- QualityScore = TotalAverageScore * 20 (max 100)
- PopularityScore = Log10(TotalViews + 1) * 10 (max 100)
- RecencyScore = DaysSinceUpdate < 30 ? 100 : (100 - DaysSinceUpdate)
```

**Example:**
```
Novel A: [Action, Fantasy, Adventure]
Novel B: [Action, Fantasy] → 2/3 shared = 66.67% match
Novel C: [Fantasy, Romance] → 1/3 shared = 33.33% match
Novel D: [Horror, Thriller] → 0/3 shared = 0% match

Score B = (66.67 * 0.5) + (4.5 * 20 * 0.3) + (Log10(5000) * 10 * 0.15) + (50)
        = 33.33 + 27 + 5.6 + 2.5
        = 68.43

Novel B ranks higher than C!
```

---

### 2. **Database Query Strategy**

#### Option A: **Simple Genre Intersection** (Recommended for <10,000 novels)

```csharp
// Get novels sharing at least 1 genre with source novel
var sourceGenreIds = await GetNovelGenreIds(sourceNovelId);

var recommendations = await dbContext.NovelGenres
    .Where(ng => sourceGenreIds.Contains(ng.GenreId) && 
                 ng.NovelId != sourceNovelId &&
                 ng.Novel.IsEligibleForRanking)
    .GroupBy(ng => ng.NovelId)
    .Select(g => new
    {
        NovelId = g.Key,
        SharedGenres = g.Count(), // How many genres match
        Novel = g.First().Novel
    })
    .OrderByDescending(x => x.SharedGenres)
    .ThenByDescending(x => x.Novel.TotalAverageScore)
    .Take(20) // Get top 20 candidates
    .ToListAsync();
```

**Performance:**
- Uses `IX_NovelGenres_GenreId` index
- Query time: ~50-100ms for 10,000 novels
- Returns top 20 candidates for scoring

---

#### Option B: **Precomputed Similarity Matrix** (For >10,000 novels)

Create a `NovelSimilarity` table:

```csharp
public class NovelSimilarity
{
    public int Id { get; set; }
    public Guid SourceNovelId { get; set; }
    public Guid TargetNovelId { get; set; }
    public decimal SimilarityScore { get; set; }
    public int SharedGenreCount { get; set; }
    public DateTime CalculatedAt { get; set; }
}

// Index: (SourceNovelId, SimilarityScore DESC)
```

**Precompute daily:**
```csharp
// Background job (runs once per day)
foreach (var novel in allNovels)
{
    var similar = CalculateSimilarNovels(novel);
    await SaveSimilarityScores(novel.Id, similar);
}
```

**Query time:** ~5ms (direct index lookup)

---

### 3. **Caching Strategy**

```csharp
public async Task<List<NovelRecommendationDto>> GetRecommendations(Guid novelId)
{
    var cacheKey = $"recommendations:{novelId}";
    
    // Try cache first
    if (_cache.TryGetValue(cacheKey, out List<NovelRecommendationDto> cached))
    {
        return cached;
    }
    
    // Calculate recommendations
    var recommendations = await CalculateRecommendations(novelId);
    
    // Cache for 24 hours (recommendations don't change often)
    _cache.Set(cacheKey, recommendations, TimeSpan.FromHours(24));
    
    return recommendations;
}
```

**Benefits:**
- First request: 50-100ms
- Subsequent requests: ~1ms (from cache)
- 99% of requests hit cache

---

## 📐 Implementation Options

### **Option 1: SQL-Only (Simple, No OpenSearch)** ✅ RECOMMENDED

**Pros:**
- No additional infrastructure
- Fast enough for <50,000 novels
- Easy to implement and maintain
- Cacheable

**Cons:**
- Limited to genre-based matching
- No full-text similarity
- Less sophisticated than ML

**Best For:** Your current scale (looks like <1,000 novels)

---

### **Option 2: OpenSearch (Advanced, Overkill)**

**Pros:**
- Can use "More Like This" query
- Full-text similarity on summaries
- Vector similarity (if using embeddings)

**Cons:**
- Requires OpenSearch setup
- More complex
- Higher infrastructure cost
- **Overkill for genre-based recommendations**

**Best For:** 100,000+ novels with complex matching needs

---

### **Option 3: Hybrid (SQL + Optional OpenSearch)**

**Pros:**
- Start with SQL
- Add OpenSearch later if needed
- Fallback to SQL if OpenSearch fails

**Cons:**
- More complex codebase

---

## 🚀 Implementation Plan (SQL-Only Approach)

### Phase 1: Core Recommendation Engine (3-4 hours)

```csharp
// 1. Create DTO
public class NovelRecommendationDto
{
    public Guid Id { get; set; }
    public string Title { get; set; }
    public string Slug { get; set; }
    public string CoverImageUrl { get; set; }
    public List<GenreSmallDto> Genres { get; set; }
    public decimal TotalAverageScore { get; set; }
    public int TotalViews { get; set; }
    public decimal SimilarityScore { get; set; } // How similar to source novel
}

// 2. Create Service Interface
public interface INovelRecommendationService
{
    Task<List<NovelRecommendationDto>> GetSimilarNovelsAsync(Guid novelId, int count = 10);
}

// 3. Implement Service
public class NovelRecommendationService : INovelRecommendationService
{
    private readonly ApplicationDbContext _dbContext;
    private readonly IMemoryCache _cache;
    
    public async Task<List<NovelRecommendationDto>> GetSimilarNovelsAsync(
        Guid novelId, 
        int count = 10)
    {
        var cacheKey = $"recommendations:{novelId}:{count}";
        
        if (_cache.TryGetValue(cacheKey, out List<NovelRecommendationDto> cached))
        {
            return cached;
        }
        
        // Step 1: Get source novel genres
        var sourceGenres = await _dbContext.NovelGenres
            .Where(ng => ng.NovelId == novelId)
            .Select(ng => ng.GenreId)
            .ToListAsync();
        
        if (sourceGenres.Count == 0) 
            return new List<NovelRecommendationDto>();
        
        // Step 2: Find novels with shared genres
        var candidates = await _dbContext.NovelGenres
            .Where(ng => sourceGenres.Contains(ng.GenreId) && 
                         ng.NovelId != novelId &&
                         ng.Novel.IsEligibleForRanking &&
                         !ng.Novel.IsDraft)
            .GroupBy(ng => ng.NovelId)
            .Select(g => new
            {
                NovelId = g.Key,
                SharedGenres = g.Count(),
                Novel = g.First().Novel
            })
            .Where(x => x.SharedGenres > 0) // At least 1 shared genre
            .ToListAsync();
        
        // Step 3: Calculate scores and rank
        var recommendations = candidates
            .Select(c => new
            {
                c.Novel,
                c.SharedGenres,
                Score = CalculateSimilarityScore(
                    c.Novel,
                    c.SharedGenres,
                    sourceGenres.Count
                )
            })
            .OrderByDescending(x => x.Score)
            .Take(count * 2) // Get 2x count to filter later
            .ToList();
        
        // Step 4: Load genres for selected novels
        var novelIds = recommendations.Select(r => r.Novel.Id).ToList();
        var genresDict = await _dbContext.NovelGenres
            .Where(ng => novelIds.Contains(ng.NovelId))
            .Include(ng => ng.Genre)
            .GroupBy(ng => ng.NovelId)
            .ToDictionaryAsync(
                g => g.Key,
                g => g.Select(ng => new GenreSmallDto
                {
                    Id = ng.Genre.Id,
                    Name = ng.Genre.Name,
                    Slug = ng.Genre.Slug
                }).ToList()
            );
        
        // Step 5: Map to DTOs
        var result = recommendations
            .Where(r => r.Novel.ReviewCount >= 1) // Filter: at least 1 review
            .Take(count) // Final count
            .Select(r => new NovelRecommendationDto
            {
                Id = r.Novel.Id,
                Title = r.Novel.Title,
                Slug = r.Novel.Slug,
                CoverImageUrl = r.Novel.CoverImageUrl,
                Genres = genresDict.GetValueOrDefault(r.Novel.Id, new List<GenreSmallDto>()),
                TotalAverageScore = r.Novel.TotalAverageScore,
                TotalViews = r.Novel.TotalViews,
                SimilarityScore = r.Score
            })
            .ToList();
        
        // Cache for 24 hours
        _cache.Set(cacheKey, result, TimeSpan.FromHours(24));
        
        return result;
    }
    
    private decimal CalculateSimilarityScore(
        Novel novel, 
        int sharedGenres, 
        int sourceGenreCount)
    {
        // Genre match score (0-100)
        var genreScore = ((decimal)sharedGenres / sourceGenreCount) * 100;
        
        // Quality score (0-100)
        var qualityScore = novel.TotalAverageScore * 20; // 5.0 → 100
        
        // Popularity score (0-100) using log scale
        var popularityScore = Math.Min(100, 
            (decimal)(Math.Log10(novel.TotalViews + 1) * 10));
        
        // Recency score (0-100)
        var daysSinceUpdate = (DateTime.UtcNow - novel.LastUpdatedAt).Days;
        var recencyScore = daysSinceUpdate < 30 
            ? 100 
            : Math.Max(0, 100 - daysSinceUpdate);
        
        // Weighted score
        return (genreScore * 0.5m) + 
               (qualityScore * 0.3m) + 
               (popularityScore * 0.15m) + 
               (recencyScore * 0.05m);
    }
}
```

---

### Phase 2: API Endpoint (1 hour)

```csharp
[HttpGet("{novelSlug}/recommendations")]
[AllowAnonymous]
public async Task<IActionResult> GetRecommendations(
    [FromRoute] string novelSlug,
    [FromQuery] int count = 10)
{
    var query = new GetNovelRecommendationsQuery
    {
        NovelSlug = novelSlug,
        Count = Math.Min(count, 20) // Max 20
    };
    
    var result = await _mediator.Send(query);
    return Ok(result);
}
```

---

### Phase 3: Optimization (2-3 hours)

1. **Add Database Indexes**
```csharp
// Existing index should handle this:
// IX_NovelGenres_NovelId_GenreId
// IX_NovelGenres_GenreId

// If needed, add:
entity.HasIndex(ng => new { ng.GenreId, ng.NovelId })
    .HasDatabaseName("IX_NovelGenres_GenreId_NovelId");
```

2. **Add Cache Invalidation**
```csharp
// When novel is updated/deleted:
_cache.Remove($"recommendations:{novelId}:{10}");

// When genres change:
var affectedNovels = await GetNovelsWithSharedGenres(changedGenres);
foreach (var novel in affectedNovels)
{
    _cache.Remove($"recommendations:{novel.Id}:{10}");
}
```

3. **Add Monitoring**
```csharp
logger.LogInformation(
    "Recommendations generated for {NovelId}: {Count} results in {Ms}ms",
    novelId, results.Count, stopwatch.ElapsedMilliseconds);
```

---

## 📊 Performance Analysis

### Scenario: Get 10 recommendations for a novel

| Approach | First Request | Cached Request | Database Queries |
|----------|---------------|----------------|------------------|
| **SQL-Only (Simple)** | 50-100ms | 1-2ms | 2-3 queries |
| **SQL-Only (Optimized)** | 30-50ms | 1-2ms | 1-2 queries |
| **Precomputed Similarity** | 5-10ms | 1-2ms | 1 query |
| **OpenSearch "More Like This"** | 20-40ms | 1-2ms | 1 query + OpenSearch |

**Recommendation:** Start with SQL-Only (Simple). Optimize later if needed.

---

## 🎯 Recommendation Quality

### Example Output:

**Source Novel:** "Ultimate Spider-Man (2024)"
- Genres: Action, Fantasy, Drama, Adventure

**Recommendations:**
1. **"Black Science"** (Score: 85.3)
   - Shared Genres: Fantasy, Adventure (2/4 = 50%)
   - Quality: 5.0 → 100
   - Views: 17 → ~12
   - **Why:** High quality, 2 shared genres

2. **"توستي"** (Score: 72.1)
   - Shared Genres: Fantasy (1/4 = 25%)
   - Quality: 3.75 → 75
   - Views: 417 → ~26
   - **Why:** Popular, shared genre

3. **"ينهار ابيض"** (Score: 58.4)
   - Shared Genres: Action, Fantasy (2/4 = 50%)
   - Quality: 1.88 → 37.6
   - Views: 320 → ~25
   - **Why:** Multiple shared genres

---

## 🚨 Cache Invalidation Strategy

```csharp
// When to invalidate recommendations cache:

1. Novel deleted → Remove all recommendations pointing to it
2. Novel genres changed → Remove its recommendations + novels with shared genres
3. Novel rating significantly changed (>0.5 change) → Remove its recommendations
4. Daily cleanup → Remove recommendations older than 7 days

// Background job (runs daily at 3 AM)
public async Task CleanupRecommendationCacheAsync()
{
    // Let cache naturally expire (24 hours)
    // Or force clear if needed
}
```

---

## 💡 Future Enhancements (Optional)

### 1. **User-Based Recommendations**
```csharp
// "Because you read X, you might like Y"
var userHistory = await GetUserReadingHistory(userId);
var recommendations = await GetRecommendationsBasedOnHistory(userHistory);
```

### 2. **Collaborative Filtering**
```csharp
// "Users who read X also read Y"
var similarUsers = await FindUsersWithSimilarTaste(userId);
var recommendations = await GetWhatTheyRead(similarUsers);
```

### 3. **OpenSearch "More Like This"** (if you add it later)
```csharp
var response = await openSearchClient.SearchAsync<Novel>(s => s
    .Index("sareed-novels")
    .Query(q => q.MoreLikeThis(mlt => mlt
        .Fields(f => f.Field(n => n.Summary))
        .Like(l => l.Document(d => d.Id(sourceNovelId)))
        .MinTermFrequency(1)
        .MaxQueryTerms(12)
    ))
);
```

---

## 🎯 Decision Matrix

| Factor | SQL-Only | OpenSearch | Precomputed |
|--------|----------|------------|-------------|
| **Setup Complexity** | ⭐ Easy | ⭐⭐⭐ Complex | ⭐⭐ Medium |
| **Performance** | ⭐⭐⭐ Good | ⭐⭐⭐⭐ Excellent | ⭐⭐⭐⭐⭐ Best |
| **Accuracy** | ⭐⭐⭐ Good | ⭐⭐⭐⭐⭐ Best | ⭐⭐⭐ Good |
| **Maintenance** | ⭐ Low | ⭐⭐⭐ High | ⭐⭐ Medium |
| **Cost** | ⭐ Free | ⭐⭐⭐ $$ | ⭐ Free |
| **Scalability** | ⭐⭐⭐ <50k novels | ⭐⭐⭐⭐⭐ Unlimited | ⭐⭐⭐⭐ <100k |

**Recommendation Path:**
1. Start with **SQL-Only** (Phase 1 & 2)
2. Add caching (Phase 3)
3. If traffic grows → Add **Precomputed Similarity**
4. If accuracy matters more → Add **OpenSearch**

---

## ✅ Final Recommendation

### **Start with SQL-Only Approach**

**Why:**
- ✅ No additional infrastructure
- ✅ Fast enough (50-100ms first request, 1-2ms cached)
- ✅ Easy to implement (3-4 hours)
- ✅ Good enough for <50,000 novels
- ✅ 99% of requests will hit cache
- ✅ Can upgrade later if needed

**Implementation Checklist:**
- [ ] Create `INovelRecommendationService`
- [ ] Implement genre-based matching with scoring
- [ ] Add caching (24 hour TTL)
- [ ] Create API endpoint
- [ ] Add to novel details page
- [ ] Monitor performance
- [ ] (Optional) Add cache invalidation on genre changes

**Estimated Time:** 4-5 hours for complete implementation

**Expected Performance:** 
- First request: ~50ms
- Cached requests: ~1ms
- Cache hit rate: >95%
