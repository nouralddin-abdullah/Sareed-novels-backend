using Application.Services;
using Domain.Entities;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Infrastructure.Services
{
    public class RankingService(ApplicationDbContext dbContext, ILogger<RankingService> logger) : IRankingService
    {
        public async Task CalculateAllGenreRankings()
        {
            logger.LogInformation("Starting calculation for all genre rankings");

            var genres = await dbContext.Genres.ToListAsync();

            foreach (var genre in genres)
            {
                // Calculate all ranking types for each genre
                await CalculateGenreRankings(genre.Id, "TopRated");      // Top Genre (All novels)
                await CalculateGenreRankings(genre.Id, "Trending");      // Trending Genre (All novels)  
                await CalculateGenreRankings(genre.Id, "New");           // New Genre (Limited to novels < 60 days)
                
                // Add small delay to prevent overwhelming the database
                await Task.Delay(100);
            }
            
            // Calculate site-wide rankings
            await CalculateSiteWideRankings();

            logger.LogInformation("Completed calculation for all genre rankings");
        }

        public async Task CalculateGenreRankings(int genreId, string rankingType = "TopRated")
        {
            logger.LogInformation("Calculating {RankingType} rankings for genre {GenreId}", rankingType, genreId);

            try
            {
                // Get novels based on ranking type
                var novelsInGenre = await GetNovelsForRankingType(genreId, rankingType);

                if (novelsInGenre.Count == 0)
                {
                    logger.LogInformation("No novels found for genre {GenreId} with ranking type {RankingType}", 
                        genreId, rankingType);
                    return;
                }

                var genreAverageRating = await CalculateGenreAverageRating(genreId);

                // Calculate scores for each novel
                foreach (var novelGenre in novelsInGenre)
                {
                    await CalculateScoresForNovel(novelGenre, genreAverageRating, rankingType);
                }

                // Save the updated scores
                await dbContext.SaveChangesAsync();

                // Create or update ranking list with proper limits
                await CreateRankingList(genreId, rankingType, novelsInGenre);

                logger.LogInformation("Successfully calculated rankings for genre {GenreId}, type {RankingType}", 
                    genreId, rankingType);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to calculate rankings for genre {GenreId}, type {RankingType}", 
                    genreId, rankingType);
            }
        }

        private async Task<List<NovelGenre>> GetNovelsForRankingType(int genreId, string rankingType)
        {
            var baseQuery = dbContext.NovelGenres
                .Include(ng => ng.Novel)
                .Where(ng => ng.GenreId == genreId && ng.Novel.IsEligibleForRanking);

            return rankingType switch
            {
                "New" => await baseQuery
                    .Where(ng => ng.Novel.CreatedAt >= DateTime.UtcNow.AddDays(-60)) // Only novels < 60 days old
                    .ToListAsync(),
                    
                "Trending" => await baseQuery
                    .Where(ng => ng.Novel.LastViewUpdate >= DateTime.UtcNow.AddDays(-30)) // Active in last 30 days
                    .ToListAsync(),
                    
                _ => await baseQuery.ToListAsync() // TopRated gets all novels
            };
        }

        private async Task CalculateSiteWideRankings()
        {
            logger.LogInformation("Calculating site-wide rankings");

            try
            {
                // TrendingNow (site-wide trending - limit to top 50)
                await CalculateTrendingNow();
                
                // AllTimeGreats (site-wide best - limit to top 100)
                await CalculateAllTimeGreats();
                
                logger.LogInformation("Successfully calculated site-wide rankings");
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to calculate site-wide rankings");
            }
        }

        private async Task CalculateTrendingNow()
        {
            // Get novels with recent activity across all genres
            var trendingNovels = await dbContext.NovelGenres
                .Include(ng => ng.Novel)
                .Where(ng => ng.Novel.IsEligibleForRanking && 
                            ng.Novel.LastViewUpdate >= DateTime.UtcNow.AddDays(-14)) // Active in last 14 days
                .GroupBy(ng => ng.NovelId)
                .Select(g => g.First()) // One entry per novel (in case novel has multiple genres)
                .ToListAsync();

            if (trendingNovels.Count == 0) return;

            // Calculate trending scores
            foreach (var novelGenre in trendingNovels)
            {
                // Trending focuses more on recent activity
                novelGenre.TrendingScore = 
                    (novelGenre.Novel.ViewsToday * 2) +           // Today's views are very important
                    (novelGenre.Novel.TotalViews * 0.05m) +      // Total views matter less
                    (novelGenre.Novel.ReviewCount * 15) +        // Recent reviews boost
                    (novelGenre.Novel.TotalAverageScore * 10);   // Quality still matters
            }

            await CreateSiteWideRankingList("TrendingNow", "Trending", trendingNovels, 50); // Limit to 50
        }

        private async Task CalculateAllTimeGreats()
        {
            // Get all novels with sufficient reviews for "all-time" consideration
            var allTimeNovels = await dbContext.NovelGenres
                .Include(ng => ng.Novel)
                .Where(ng => ng.Novel.IsEligibleForRanking && 
                            ng.Novel.ReviewCount >= 25) // Minimum 25 reviews for "all-time" status
                .GroupBy(ng => ng.NovelId)
                .Select(g => g.First()) // One entry per novel
                .ToListAsync();

            if (allTimeNovels.Count == 0) return;

            // Calculate all-time scores (focus on quality and proven popularity)
            foreach (var novelGenre in allTimeNovels)
            {
                novelGenre.QualityScore = novelGenre.Novel.TotalAverageScore; // No Bayesian needed (enough reviews)
                novelGenre.PopularityScore = 
                    (novelGenre.Novel.ReviewCount * 20) +        // Review count is very important
                    (novelGenre.Novel.TotalViews * 0.02m);       // Views matter but less than reviews
                    
                // All-time score balances quality and proven popularity
                var allTimeScore = (novelGenre.QualityScore * 0.6m) + (novelGenre.PopularityScore * 0.4m);
                novelGenre.GenreScore = allTimeScore;
            }

            await CreateSiteWideRankingList("AllTimeGreats", "AllTime", allTimeNovels, 100); // Limit to 100
        }

        private static Task CalculateScoresForNovel(NovelGenre novelGenre, decimal genreAverageRating, string rankingType)
        {
            var novel = novelGenre.Novel;
            var minimumReviews = 10;

            // Calculate Quality Score (Bayesian Average)
            if (novel.ReviewCount >= minimumReviews)
            {
                novelGenre.QualityScore = novel.TotalAverageScore;
            }
            else
            {
                novelGenre.QualityScore = ((novel.ReviewCount * novel.TotalAverageScore) + (minimumReviews * genreAverageRating))
                                         / (novel.ReviewCount + minimumReviews);
            }

            // Calculate scores based on ranking type
            switch (rankingType)
            {
                case "Trending":
                    // Trending emphasizes recent activity
                    novelGenre.PopularityScore = (novel.ViewsToday * 5) + (novel.TotalViews * 0.02m) + (novel.ReviewCount * 8);
                    novelGenre.TrendingScore = (novelGenre.QualityScore * 0.4m) + (novelGenre.PopularityScore * 0.6m);
                    novelGenre.GenreScore = novelGenre.TrendingScore;
                    break;
                    
                case "New":
                    // New novels get a boost but still need some quality
                    var daysSinceCreated = (DateTime.UtcNow - novel.CreatedAt).Days;
                    var newBonus = Math.Max(0, (60 - daysSinceCreated) * 0.05m); // Bonus decreases with age
                    novelGenre.PopularityScore = novel.TotalViews * 0.1m + novel.ReviewCount * 12;
                    novelGenre.GenreScore = novelGenre.QualityScore + newBonus + (novelGenre.PopularityScore * 0.2m);
                    break;
                    
                default: // TopRated
                    novelGenre.PopularityScore = novel.TotalViews * 0.1m + novel.ReviewCount * 10;
                    novelGenre.TrendingScore = novelGenre.QualityScore * 0.7m + novelGenre.PopularityScore * 0.3m;
                    novelGenre.GenreScore = novelGenre.QualityScore; // Top rated focuses on quality
                    break;
            }

            novelGenre.LastRankUpdate = DateTime.UtcNow;
            return Task.CompletedTask;
        }

        private async Task CreateRankingList(int genreId, string rankingType, List<NovelGenre> novelsInGenre)
        {
            // Get the appropriate limit for this ranking type
            var limit = GetLimitForRankingType(rankingType);

            // Find or create ranking list
            var rankingList = await dbContext.RankingLists
                .Include(rl => rl.Entries)
                .FirstOrDefaultAsync(rl => rl.GenreId == genreId && rl.RankingType == rankingType);

            if (rankingList == null)
            {
                var genre = await dbContext.Genres.FindAsync(genreId);
                rankingList = new RankingList
                {
                    Name = GetRankingListName(genre?.Name ?? "Unknown", rankingType),
                    GenreId = genreId,
                    RankingType = rankingType
                };
                await dbContext.RankingLists.AddAsync(rankingList);
                await dbContext.SaveChangesAsync();
            }

            // Clear existing entries
            if (rankingList.Entries.Count > 0)
            {
                dbContext.RankingEntries.RemoveRange(rankingList.Entries);
            }

            // Sort and limit novels
            var sortedNovels = novelsInGenre
                .OrderByDescending(ng => ng.GenreScore ?? 0)
                .Take(limit) // Apply appropriate limit
                .ToList();

            var rank = 1;
            foreach (var novelGenre in sortedNovels)
            {
                var entry = new RankingEntry
                {
                    RankingListId = rankingList.Id,
                    NovelId = novelGenre.NovelId,
                    Rank = rank++,
                    Score = novelGenre.GenreScore ?? 0,
                    QualityScore = novelGenre.QualityScore,
                    PopularityScore = novelGenre.PopularityScore,
                    TrendingScore = novelGenre.TrendingScore
                };

                await dbContext.RankingEntries.AddAsync(entry);
            }

            rankingList.LastUpdated = DateTime.UtcNow;
            rankingList.TotalNovels = sortedNovels.Count;
            await dbContext.SaveChangesAsync();
        }

        private async Task CreateSiteWideRankingList(string name, string rankingType, List<NovelGenre> novels, int limit)
        {
            var rankingList = await dbContext.RankingLists
                .Include(rl => rl.Entries)
                .FirstOrDefaultAsync(rl => rl.GenreId == null && rl.RankingType == rankingType);

            if (rankingList == null)
            {
                rankingList = new RankingList
                {
                    Name = name,
                    GenreId = null, // Site-wide
                    RankingType = rankingType
                };
                await dbContext.RankingLists.AddAsync(rankingList);
                await dbContext.SaveChangesAsync();
            }

            if (rankingList.Entries.Count > 0)
            {
                dbContext.RankingEntries.RemoveRange(rankingList.Entries);
            }

            var sortedNovels = novels
                .OrderByDescending(ng => ng.GenreScore ?? 0)
                .Take(limit)
                .ToList();

            var rank = 1;
            foreach (var novelGenre in sortedNovels)
            {
                var entry = new RankingEntry
                {
                    RankingListId = rankingList.Id,
                    NovelId = novelGenre.NovelId,
                    Rank = rank++,
                    Score = novelGenre.GenreScore ?? 0,
                    QualityScore = novelGenre.QualityScore,
                    PopularityScore = novelGenre.PopularityScore,
                    TrendingScore = novelGenre.TrendingScore
                };

                await dbContext.RankingEntries.AddAsync(entry);
            }

            rankingList.LastUpdated = DateTime.UtcNow;
            rankingList.TotalNovels = sortedNovels.Count;
            await dbContext.SaveChangesAsync();
        }

        private static int GetLimitForRankingType(string rankingType)
        {
            return rankingType switch
            {
                "New" => 30,        // Limit New novels to top 30 per genre
                "Trending" => 50,   // Limit Trending to top 50 per genre  
                _ => int.MaxValue   // TopRated shows all novels
            };
        }

        private static string GetRankingListName(string genreName, string rankingType)
        {
            return rankingType switch
            {
                "Trending" => $"Trending{genreName}",
                "New" => $"New{genreName}",
                _ => $"Top{genreName}"
            };
        }

        private async Task<decimal> CalculateGenreAverageRating(int genreId)
        {
            var genreNovels = await dbContext.NovelGenres
                .Include(ng => ng.Novel)
                .Where(ng => ng.GenreId == genreId &&
                       ng.Novel.ReviewCount > 0 &&
                       ng.Novel.IsEligibleForRanking)
                .Select(ng => ng.Novel.TotalAverageScore)
                .ToListAsync();

            if (genreNovels.Count == 0)
            {
                logger.LogWarning("No novels with reviews found for genre {GenreId}, using default average", genreId);
                return 3.5m;
            }

            var average = genreNovels.Average();
            logger.LogInformation("Genre {GenreId} average rating: {Average:F2} (from {Count} novels)",
                genreId, average, genreNovels.Count);

            return average;
        }
    }
}
