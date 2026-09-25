namespace Domain.Ranking;

/// <summary>Everything the ranking needs to know about one novel. The author's own activity is already excluded.</summary>
public sealed class NovelSignals
{
    public required Guid NovelId { get; init; }
    public required int PublishedChapters { get; init; }
    public required DateTime FirstChapterAt { get; init; }
    public required DateTime LastChapterAt { get; init; }

    /// <summary>Most recent activity time of each distinct reader (signed-in progress or tracked chapter reads).</summary>
    public IReadOnlyCollection<DateTime> ReaderActivity { get; init; } = [];

    /// <summary>For each signed-in reader, the share of published chapters they reached (0..1).</summary>
    public IReadOnlyCollection<double> ReaderDepths { get; init; } = [];

    /// <summary>Novel page views per UTC day (recent window only).</summary>
    public IReadOnlyCollection<DailyViews> DailyViews { get; init; } = [];

    /// <summary>Creation time of each comment by someone other than the author (recent window only).</summary>
    public IReadOnlyCollection<DateTime> Comments { get; init; } = [];

    /// <summary>Review scores (0-5) from reviewers who actually read the novel.</summary>
    public IReadOnlyCollection<decimal> TrustedRatings { get; init; } = [];
}

public readonly record struct DailyViews(DateTime Day, int Views);

/// <summary>
/// Reader-first ranking, tuned on production data (2026-09-24). Signed-in readers and how far they read are the
/// signals people can't cheaply fake; page views are square-rooted per day so a refresh-spam day counts like a
/// handful of views; everything recent decays with a 3.5-day half-life; reviews only count when the reviewer read.
/// </summary>
public static class RankingFormula
{
    public const double HalfLifeDays = 3.5;
    public const int NewWindowDays = 60;
    public const int QualityListMinChapters = 3;

    private const double ReaderWeight = 3;
    private const double ViewWeight = 1;
    private const double CommentWeight = 2;
    private const double FreshChapterWeight = 1.5;
    private const double ReaderCountTieBreak = 0.01;

    private const double DepthPrior = 0.3;
    private const double DepthPriorWeight = 5;
    private const double RatingPriorWeight = 5;
    private const double NewBonusWeight = 2;
    private const double NewBonusHalfLifeDays = 14;

    /// <summary>1 now, 0.5 after one half-life, and so on. Future timestamps count as now.</summary>
    public static double Decay(DateTime at, DateTime now, double halfLifeDays = HalfLifeDays)
    {
        var ageDays = Math.Max(0, (now - at).TotalDays);
        return Math.Pow(0.5, ageDays / halfLifeDays);
    }

    /// <summary>What people are reading right now.</summary>
    public static double Trending(NovelSignals s, DateTime now) =>
        ReaderWeight * s.ReaderActivity.Sum(at => Decay(at, now))
        + ViewWeight * s.DailyViews.Sum(d => Math.Sqrt(Math.Max(0, d.Views)) * Decay(d.Day, now))
        + CommentWeight * s.Comments.Sum(at => Decay(at, now))
        + FreshChapterWeight * Decay(s.LastChapterAt, now)
        + ReaderCountTieBreak * s.ReaderActivity.Count;

    /// <summary>Average share of the novel its readers get through, pulled toward 0.3 when few people have read it.</summary>
    public static double DepthBayes(NovelSignals s) =>
        (s.ReaderDepths.Sum(d => Math.Clamp(d, 0, 1)) + DepthPrior * DepthPriorWeight)
        / (s.ReaderDepths.Count + DepthPriorWeight);

    /// <summary>Trusted average rating, pulled toward the site-wide average when there are few reviews.</summary>
    public static double BayesRating(NovelSignals s, double prior) =>
        ((double)s.TrustedRatings.Sum() + prior * RatingPriorWeight) / (s.TrustedRatings.Count + RatingPriorWeight);

    /// <summary>Most-read novels that keep their readers.</summary>
    public static double AllTime(NovelSignals s, double ratingPrior) =>
        Math.Log(1 + s.ReaderActivity.Count) * (0.5 + DepthBayes(s))
        + 0.25 * (BayesRating(s, ratingPrior) - ratingPrior);

    /// <summary>Quality: trusted ratings and read-through, both smoothed.</summary>
    public static double TopRated(NovelSignals s, double ratingPrior) =>
        0.6 * BayesRating(s, ratingPrior) / 5 + 0.4 * DepthBayes(s);

    /// <summary>Trending among recent novels, with a head start that fades over a few weeks.</summary>
    public static double New(NovelSignals s, DateTime now) =>
        Trending(s, now) + NewBonusWeight * Decay(s.FirstChapterAt, now, NewBonusHalfLifeDays);

    public static bool IsNew(NovelSignals s, DateTime now) => (now - s.FirstChapterAt).TotalDays <= NewWindowDays;

    public static bool QualifiesForQualityLists(NovelSignals s) => s.PublishedChapters >= QualityListMinChapters;
}
