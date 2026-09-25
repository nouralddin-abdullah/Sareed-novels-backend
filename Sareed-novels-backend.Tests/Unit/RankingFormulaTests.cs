using Domain.Ranking;

namespace Sareed_novels_backend.Tests.Unit;

public class RankingFormulaTests
{
    private static readonly DateTime Now = new(2026, 9, 24, 12, 0, 0, DateTimeKind.Utc);

    private static NovelSignals Novel(
        int chapters = 10,
        double firstChapterDaysAgo = 200,
        double lastChapterDaysAgo = 100,
        IEnumerable<double>? readerDaysAgo = null,
        IEnumerable<double>? depths = null,
        IEnumerable<(double DaysAgo, int Views)>? views = null,
        IEnumerable<double>? commentDaysAgo = null,
        IEnumerable<decimal>? ratings = null) => new()
    {
        NovelId = Guid.NewGuid(),
        PublishedChapters = chapters,
        FirstChapterAt = Now.AddDays(-firstChapterDaysAgo),
        LastChapterAt = Now.AddDays(-lastChapterDaysAgo),
        ReaderActivity = (readerDaysAgo ?? []).Select(d => Now.AddDays(-d)).ToList(),
        ReaderDepths = (depths ?? []).ToList(),
        DailyViews = (views ?? []).Select(v => new DailyViews(Now.AddDays(-v.DaysAgo), v.Views)).ToList(),
        Comments = (commentDaysAgo ?? []).Select(d => Now.AddDays(-d)).ToList(),
        TrustedRatings = (ratings ?? []).ToList()
    };

    [Fact]
    public void Decay_halves_every_three_and_a_half_days()
    {
        Assert.Equal(1, RankingFormula.Decay(Now, Now), 6);
        Assert.Equal(0.5, RankingFormula.Decay(Now.AddDays(-3.5), Now), 6);
        Assert.Equal(0.25, RankingFormula.Decay(Now.AddDays(-7), Now), 6);
        Assert.Equal(1, RankingFormula.Decay(Now.AddDays(1), Now), 6); // future counts as now
    }

    [Fact]
    public void A_one_day_view_spike_loses_to_steady_daily_readership()
    {
        // The production pattern: 104 views in one day, almost no readers.
        var spike = Novel(views: [(1, 104)]);
        var steady = Novel(views: Enumerable.Range(1, 7).Select(d => ((double)d, 10)));

        Assert.True(RankingFormula.Trending(steady, Now) > RankingFormula.Trending(spike, Now));
    }

    [Fact]
    public void Recent_readers_count_more_than_old_readers()
    {
        var recent = Novel(readerDaysAgo: [0.5, 1, 2]);
        var old = Novel(readerDaysAgo: [30, 31, 32]);

        Assert.True(RankingFormula.Trending(recent, Now) > RankingFormula.Trending(old, Now));
    }

    [Fact]
    public void A_handful_of_active_readers_beats_a_moderate_view_count()
    {
        var readers = Novel(readerDaysAgo: [1, 1, 2]);
        var viewsOnly = Novel(views: [(1, 20), (2, 20)]);

        Assert.True(RankingFormula.Trending(readers, Now) > RankingFormula.Trending(viewsOnly, Now));
    }

    [Fact]
    public void Publishing_a_chapter_gives_a_fading_boost()
    {
        var justUpdated = Novel(lastChapterDaysAgo: 0);
        var stale = Novel(lastChapterDaysAgo: 60);

        Assert.True(RankingFormula.Trending(justUpdated, Now) > RankingFormula.Trending(stale, Now));
    }

    [Fact]
    public void Depth_and_rating_fall_back_to_their_priors_without_data()
    {
        var empty = Novel();
        Assert.Equal(0.3, RankingFormula.DepthBayes(empty), 6);
        Assert.Equal(3.7, RankingFormula.BayesRating(empty, prior: 3.7), 6);
    }

    [Fact]
    public void A_few_perfect_reviews_are_pulled_toward_the_site_average()
    {
        // Mirrors the gamed novel: five 5.0 reviews shouldn't read as a perfect 5.
        var novel = Novel(ratings: [5, 5, 5, 5, 5]);
        var rating = RankingFormula.BayesRating(novel, prior: 3.5);

        Assert.InRange(rating, 4.0, 4.5);
    }

    [Fact]
    public void All_time_favors_novels_whose_readers_keep_reading()
    {
        var keepsReaders = Novel(readerDaysAgo: Enumerable.Repeat(90.0, 10), depths: Enumerable.Repeat(0.9, 10));
        var losesReaders = Novel(readerDaysAgo: Enumerable.Repeat(90.0, 10), depths: Enumerable.Repeat(0.05, 10));

        Assert.True(RankingFormula.AllTime(keepsReaders, 3.5) > RankingFormula.AllTime(losesReaders, 3.5));
    }

    [Fact]
    public void New_novels_get_a_head_start_that_fades()
    {
        var brandNew = Novel(firstChapterDaysAgo: 1, lastChapterDaysAgo: 1);
        var monthOld = Novel(firstChapterDaysAgo: 40, lastChapterDaysAgo: 1);

        Assert.True(RankingFormula.New(brandNew, Now) > RankingFormula.New(monthOld, Now));
        Assert.True(RankingFormula.IsNew(monthOld, Now));
        Assert.False(RankingFormula.IsNew(Novel(firstChapterDaysAgo: 61), Now));
    }

    [Theory]
    [InlineData(1, false)]
    [InlineData(2, false)]
    [InlineData(3, true)]
    public void Quality_lists_need_at_least_three_chapters(int chapters, bool qualifies) =>
        Assert.Equal(qualifies, RankingFormula.QualifiesForQualityLists(Novel(chapters: chapters)));
}
