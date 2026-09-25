using System.Diagnostics;
using Application.Services;
using Domain.Entities;
using Domain.Ranking;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Infrastructure.Services;

/// <summary>
/// Recomputes every ranking list from reading activity (see <see cref="RankingFormula"/>) and rewrites the stored
/// RankingLists/RankingEntries in one transaction, so the public ranking endpoints never see a half-written list.
/// Lists that end up with no qualifying novels are emptied rather than left stale.
/// </summary>
public class RankingService(ApplicationDbContext dbContext, TimeProvider timeProvider, ILogger<RankingService> logger) : IRankingService
{
    private const string Published = "Published";
    private const int SignalWindowDays = 60;
    private const int SiteWideLimit = 100;
    private const int GenreLimit = 50;
    private const double DefaultRatingPrior = 3.5;

    /// <summary>All lists come from one pass over the data, so a single genre is recomputed along with the rest.</summary>
    public Task CalculateGenreRankings(int genreId, string rankingType = RankingTypes.TopRated) => CalculateAllGenreRankings();

    public async Task CalculateAllGenreRankings()
    {
        var stopwatch = Stopwatch.StartNew();
        var now = timeProvider.GetUtcNow().UtcDateTime;

        var lists = await ComputeListsAsync(now);
        await SaveListsAsync(lists, now);

        logger.LogInformation(
            "Rankings recalculated: {Lists} lists, {Entries} entries in {ElapsedMs} ms",
            lists.Count, lists.Sum(l => l.Entries.Count), stopwatch.ElapsedMilliseconds);
    }

    /// <summary>Computes every list without writing anything (the job saves them; diagnostics can just look).</summary>
    public async Task<IReadOnlyList<ComputedList>> ComputeListsAsync(DateTime now)
    {
        var signals = await LoadSignalsAsync(now);
        var trustedRatings = signals.SelectMany(s => s.TrustedRatings).ToList();
        var ratingPrior = trustedRatings.Count > 0 ? (double)trustedRatings.Average() : DefaultRatingPrior;

        var genres = await dbContext.Genres.AsNoTracking()
            .Select(g => new { g.Id, g.Name })
            .ToListAsync();
        var novelsByGenre = (await dbContext.NovelGenres.AsNoTracking()
                .Select(ng => new { ng.GenreId, ng.NovelId })
                .ToListAsync())
            .ToLookup(ng => ng.GenreId, ng => ng.NovelId);

        var lists = new List<ComputedList>
        {
            new(null, RankingTypes.Trending, "TrendingNow",
                Rank(signals, s => RankingFormula.Trending(s, now), now, ratingPrior, SiteWideLimit)),
            new(null, RankingTypes.AllTime, "AllTimeGreats",
                Rank(signals.Where(s => RankingFormula.QualifiesForQualityLists(s) && s.ReaderActivity.Count > 0),
                    s => RankingFormula.AllTime(s, ratingPrior), now, ratingPrior, SiteWideLimit))
        };

        foreach (var genre in genres)
        {
            var ids = novelsByGenre[genre.Id].ToHashSet();
            var inGenre = signals.Where(s => ids.Contains(s.NovelId)).ToList();

            lists.Add(new(genre.Id, RankingTypes.Trending, $"Trending{genre.Name}",
                Rank(inGenre, s => RankingFormula.Trending(s, now), now, ratingPrior, GenreLimit)));
            lists.Add(new(genre.Id, RankingTypes.TopRated, $"Top{genre.Name}",
                Rank(inGenre.Where(RankingFormula.QualifiesForQualityLists),
                    s => RankingFormula.TopRated(s, ratingPrior), now, ratingPrior, GenreLimit)));
            lists.Add(new(genre.Id, RankingTypes.New, $"New{genre.Name}",
                Rank(inGenre.Where(s => RankingFormula.IsNew(s, now)),
                    s => RankingFormula.New(s, now), now, ratingPrior, GenreLimit)));
        }

        return lists;
    }

    private async Task<List<NovelSignals>> LoadSignalsAsync(DateTime now)
    {
        var since = now.AddDays(-SignalWindowDays);

        var novels = await dbContext.Novels.AsNoTracking()
            .Where(n => !n.IsDraft && n.IsEligibleForRanking)
            .Select(n => new
            {
                n.Id,
                n.AuthorId,
                PublishedChapters = n.Chapters.Count(c => c.Status == Published),
                FirstChapterAt = n.Chapters.Where(c => c.Status == Published).Min(c => (DateTime?)c.CreatedAt),
                LastChapterAt = n.Chapters.Where(c => c.Status == Published).Max(c => (DateTime?)c.CreatedAt)
            })
            .Where(n => n.PublishedChapters > 0)
            .ToListAsync();

        var progress = (await dbContext.UserNovelProgress.AsNoTracking()
                .Select(p => new { p.NovelId, p.UserId, p.LastReadAt, p.LastReadChapterNumber })
                .ToListAsync())
            .ToLookup(p => p.NovelId);

        var chapterVisitors = (await dbContext.DailyUniqueViews.AsNoTracking()
                .Where(v => v.Kind == ViewKind.Chapter && v.Day >= since)
                .GroupBy(v => new { v.NovelId, v.VisitorKey })
                .Select(g => new { g.Key.NovelId, g.Key.VisitorKey, LastDay = g.Max(v => v.Day) })
                .ToListAsync())
            .ToLookup(v => v.NovelId);

        var dailyViews = (await dbContext.NovelViews.AsNoTracking()
                .Where(v => v.ViewDate >= since)
                .Select(v => new { v.NovelId, v.ViewDate, v.ViewCount })
                .ToListAsync())
            .ToLookup(v => v.NovelId);

        // Chapter and paragraph comments (post comments aren't about a novel).
        var comments = (await dbContext.Comments.AsNoTracking()
                .Where(c => c.CreatedAt >= since && c.PostId == null)
                .Select(c => new
                {
                    c.UserId,
                    c.CreatedAt,
                    ChapterId = c.ChapterId ?? (c.Paragraph != null ? c.Paragraph.ChapterId : (Guid?)null)
                })
                .Join(dbContext.Chapters, c => c.ChapterId, ch => (Guid?)ch.Id,
                    (c, ch) => new { ch.NovelId, c.UserId, c.CreatedAt })
                .ToListAsync())
            .ToLookup(c => c.NovelId);

        var reviews = (await dbContext.Reviews.AsNoTracking()
                .Select(r => new { r.NovelId, r.ReviewerId, r.TotalAverageScore })
                .ToListAsync())
            .ToLookup(r => r.NovelId);

        var result = new List<NovelSignals>(novels.Count);
        foreach (var novel in novels)
        {
            var authorKey = $"u:{novel.AuthorId}";
            var lastActivity = new Dictionary<string, DateTime>();
            var depths = new List<double>();
            var chapterReached = new Dictionary<string, int>();

            foreach (var p in progress[novel.Id].Where(p => p.UserId != novel.AuthorId))
            {
                Touch(lastActivity, $"u:{p.UserId}", p.LastReadAt);
                depths.Add(Math.Min(p.LastReadChapterNumber, novel.PublishedChapters) / (double)novel.PublishedChapters);
                chapterReached[p.UserId] = p.LastReadChapterNumber;
            }

            foreach (var v in chapterVisitors[novel.Id].Where(v => v.VisitorKey != authorKey))
            {
                Touch(lastActivity, v.VisitorKey, v.LastDay);
            }

            // A review counts only if the reviewer got at least 3 chapters in (or finished a shorter novel).
            var minChaptersForReview = Math.Min(3, novel.PublishedChapters);

            result.Add(new NovelSignals
            {
                NovelId = novel.Id,
                PublishedChapters = novel.PublishedChapters,
                FirstChapterAt = novel.FirstChapterAt!.Value,
                LastChapterAt = novel.LastChapterAt!.Value,
                ReaderActivity = lastActivity.Values.ToList(),
                ReaderDepths = depths,
                DailyViews = dailyViews[novel.Id].Select(v => new DailyViews(v.ViewDate, v.ViewCount)).ToList(),
                Comments = comments[novel.Id].Where(c => c.UserId != novel.AuthorId).Select(c => c.CreatedAt).ToList(),
                TrustedRatings = reviews[novel.Id]
                    .Where(r => r.ReviewerId != novel.AuthorId
                                && chapterReached.TryGetValue(r.ReviewerId, out var reached)
                                && reached >= minChaptersForReview)
                    .Select(r => r.TotalAverageScore)
                    .ToList()
            });
        }

        return result;
    }

    private static void Touch(Dictionary<string, DateTime> lastActivity, string key, DateTime at)
    {
        if (!lastActivity.TryGetValue(key, out var existing) || at > existing)
        {
            lastActivity[key] = at;
        }
    }

    private static List<ComputedEntry> Rank(
        IEnumerable<NovelSignals> candidates, Func<NovelSignals, double> score, DateTime now, double ratingPrior, int limit) =>
        candidates
            .Select(s => new { Signals = s, Score = score(s) })
            .OrderByDescending(x => x.Score)
            .ThenByDescending(x => x.Signals.ReaderActivity.Count)
            .ThenByDescending(x => x.Signals.LastChapterAt)
            .ThenBy(x => x.Signals.NovelId)
            .Take(limit)
            .Select(x => new ComputedEntry(
                x.Signals.NovelId,
                x.Score,
                RankingFormula.BayesRating(x.Signals, ratingPrior),
                x.Signals.ReaderActivity.Count,
                RankingFormula.Trending(x.Signals, now)))
            .ToList();

    private async Task SaveListsAsync(IReadOnlyList<ComputedList> lists, DateTime now)
    {
        await using var transaction = await dbContext.Database.BeginTransactionAsync();

        // During an IIS app-pool recycle two processes can run this at once; only one may write.
        var lockResult = await dbContext.Database
            .SqlQueryRaw<int>("""
                DECLARE @result int;
                EXEC @result = sp_getapplock @Resource = 'sard-ranking-recalculation', @LockMode = 'Exclusive',
                                             @LockOwner = 'Transaction', @LockTimeout = 0;
                SELECT @result AS Value;
                """)
            .ToListAsync();
        if (lockResult.FirstOrDefault() < 0)
        {
            logger.LogInformation("Another ranking recalculation is writing; skipping this run");
            return;
        }

        var existing = await dbContext.RankingLists.ToListAsync();
        var byKey = new Dictionary<(int?, string), RankingList>();
        foreach (var list in lists)
        {
            var rankingList = existing.FirstOrDefault(l => l.GenreId == list.GenreId && l.RankingType == list.Type);
            if (rankingList == null)
            {
                rankingList = new RankingList { GenreId = list.GenreId, RankingType = list.Type, Name = list.Name };
                dbContext.RankingLists.Add(rankingList);
            }

            rankingList.Name = list.Name;
            rankingList.LastUpdated = now;
            rankingList.TotalNovels = list.Entries.Count;
            byKey[(list.GenreId, list.Type)] = rankingList;
        }

        // Lists we no longer produce (e.g. a removed genre) are emptied, never left showing stale entries.
        foreach (var stale in existing.Where(l => !byKey.ContainsKey((l.GenreId, l.RankingType))))
        {
            stale.TotalNovels = 0;
            stale.LastUpdated = now;
        }

        await dbContext.SaveChangesAsync();
        await dbContext.RankingEntries.ExecuteDeleteAsync();

        foreach (var list in lists)
        {
            var rankingListId = byKey[(list.GenreId, list.Type)].Id;
            var rank = 1;
            foreach (var entry in list.Entries)
            {
                dbContext.RankingEntries.Add(new RankingEntry
                {
                    RankingListId = rankingListId,
                    NovelId = entry.NovelId,
                    Rank = rank++,
                    Score = ToDecimal(entry.Score, 99_999_999m),
                    QualityScore = ToDecimal(entry.Quality, 999m),
                    PopularityScore = ToDecimal(entry.Readers, 99_999_999m),
                    TrendingScore = ToDecimal(entry.Trending, 99_999_999m),
                    CreatedAt = now
                });
            }
        }

        await dbContext.SaveChangesAsync();
        await transaction.CommitAsync();
    }

    private static decimal ToDecimal(double value, decimal max) =>
        double.IsFinite(value) ? Math.Clamp(Math.Round((decimal)value, 2), -max, max) : 0m;

    public sealed record ComputedList(int? GenreId, string Type, string Name, List<ComputedEntry> Entries);

    public sealed record ComputedEntry(Guid NovelId, double Score, double Quality, double Readers, double Trending);
}
