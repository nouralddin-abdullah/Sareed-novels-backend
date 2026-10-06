using Application.Privileges;
using Domain.Constants;
using Domain.Entities;
using Domain.Repositories;

namespace Sareed_novels_backend.Tests.Unit;

/// <summary>
/// The rules of early access (#94): a chapter is locked by its own lock (when it started, whether it was freed), for the
/// novel's days or for subscribers only, while early access is on; the first 10 published chapters stay free. Through
/// the API: Integration/EarlyAccessHttpTests.
/// </summary>
public class EarlyAccessRulesTests
{
    private static readonly DateTime From = new(2026, 10, 6, 21, 30, 0, DateTimeKind.Utc);
    private static readonly EarlyAccessSettings SevenDays = new(true, 7, false);
    private static readonly EarlyAccessSettings SubscribersOnly = new(true, null, true);

    private static ChapterLock Locked(DateTime? from = null, DateTime? freedAt = null, bool published = true) =>
        new(published, from ?? From, freedAt);

    [Fact]
    public void A_lock_lasts_its_days_from_its_own_start_to_the_instant()
    {
        var end = From.AddDays(7);

        Assert.True(EarlyAccess.IsLocked(Locked(), SevenDays, From));
        Assert.True(EarlyAccess.IsLocked(Locked(), SevenDays, end.AddTicks(-1)));
        Assert.False(EarlyAccess.IsLocked(Locked(), SevenDays, end));
        Assert.False(EarlyAccess.IsLocked(Locked(), SevenDays, end.AddYears(1)));

        Assert.Equal(end, EarlyAccess.UnlocksAt(Locked(), SevenDays, From.AddDays(3)));
        Assert.Equal(DateTimeKind.Utc, EarlyAccess.UnlocksAt(Locked(), SevenDays, From)!.Value.Kind);
        Assert.Null(EarlyAccess.UnlocksAt(Locked(), SevenDays, end));

        // One day, and thirty.
        Assert.False(EarlyAccess.IsLocked(Locked(), new EarlyAccessSettings(true, 1, false), From.AddDays(1)));
        Assert.True(EarlyAccess.IsLocked(Locked(), new EarlyAccessSettings(true, 30, false), From.AddDays(29)));
    }

    [Fact]
    public void Subscribers_only_never_ends_by_itself()
    {
        Assert.True(EarlyAccess.IsLocked(Locked(), SubscribersOnly, From.AddYears(5)));
        Assert.Null(EarlyAccess.UnlocksAt(Locked(), SubscribersOnly, From.AddYears(5)));
    }

    [Fact]
    public void Nothing_is_locked_while_off_unpublished_never_locked_or_freed()
    {
        Assert.False(EarlyAccess.IsLocked(Locked(), new EarlyAccessSettings(false, 7, false), From));
        Assert.False(EarlyAccess.IsLocked(Locked(), null, From));
        Assert.False(EarlyAccess.IsLocked(Locked(published: false), SevenDays, From));
        Assert.False(EarlyAccess.IsLocked(new ChapterLock(true, null, null), SubscribersOnly, From));
        Assert.False(EarlyAccess.IsLocked(Locked(freedAt: From.AddHours(1)), SubscribersOnly, From.AddHours(2)));
        Assert.Null(EarlyAccess.UnlocksAt(Locked(freedAt: From.AddHours(1)), SevenDays, From.AddHours(2)));
    }

    [Theory]
    [InlineData(1, false)]
    [InlineData(10, false)]
    [InlineData(11, true)]
    [InlineData(500, true)]
    public void A_chapter_coming_out_among_the_first_ten_stays_free(int publishedSequence, bool locks) =>
        Assert.Equal(locks, EarlyAccess.LocksWhenItComesOut(publishedSequence));

    [Theory]
    [InlineData(0, null)]
    [InlineData(10, null)]
    [InlineData(11, 11)]
    [InlineData(25, 11)]
    [InlineData(30, 11)]
    [InlineData(31, 12)]
    [InlineData(60, 41)]
    public void Enabling_locks_the_last_twenty_by_default_never_the_first_ten(int published, int? start)
    {
        Assert.Equal(start, EarlyAccess.DefaultStart(published));
        if (start is { } first)
        {
            Assert.InRange(published - first + 1, 1, EarlyAccess.MaxLockedWhenEnabled);
        }
    }

    [Fact]
    public void The_summary_counts_the_locked_chapters_from_the_chapters()
    {
        var now = From.AddDays(5);
        PublishedChapterLock Row(int sequence, DateTime? from, DateTime? freedAt = null) => new(Guid.NewGuid(), sequence, from, freedAt);
        List<PublishedChapterLock> chapters =
        [
            Row(9, null),
            Row(10, From.AddDays(-3)), // over
            Row(11, From.AddDays(-1)), // ends From + 6
            Row(12, From.AddHours(1), freedAt: From.AddHours(2)), // freed
            Row(13, From.AddHours(-12)), // ends From + 6.5: after 11's
            Row(14, From)
        ];

        var summary = EarlyAccessSummary.Of(chapters, SevenDays, now);
        Assert.Equal((3, From.AddDays(6), 11, 6), (summary.LockedCount, summary.NextUnlockAt, summary.FirstLockedSequence, summary.PublishedCount));

        var onlySubscribers = EarlyAccessSummary.Of(chapters, SubscribersOnly, now);
        Assert.Equal((4, (DateTime?)null, 10), (onlySubscribers.LockedCount, onlySubscribers.NextUnlockAt, onlySubscribers.FirstLockedSequence));

        var off = EarlyAccessSummary.Of(chapters, new EarlyAccessSettings(false, 7, false), now);
        Assert.Equal((0, (DateTime?)null, (int?)null, 6), (off.LockedCount, off.NextUnlockAt, off.FirstLockedSequence, off.PublishedCount));
    }

    [Fact]
    public void A_view_opens_every_chapter_to_the_author_and_subscribers_and_tells_others_when_it_opens()
    {
        var chapter = new Chapter { Status = ChapterStatuses.Published, EarlyAccessFrom = From };
        var free = new Chapter { Status = ChapterStatuses.Published };
        var now = From.AddDays(1);

        var reader = new EarlyAccessView(SevenDays, readsLockedChapters: false, now);
        Assert.Equal((true, true, From.AddDays(7)), (reader.IsLocked(chapter), reader.IsLockedForViewer(chapter), reader.UnlocksAtForViewer(chapter)));
        Assert.Equal((false, (DateTime?)null), (reader.IsLockedForViewer(free), reader.UnlocksAtForViewer(free)));

        // The author's lists say what non-subscribers meet; the author and subscribers read it.
        var subscriber = new EarlyAccessView(SevenDays, readsLockedChapters: true, now);
        Assert.Equal((true, From.AddDays(7)), (subscriber.IsLocked(chapter), subscriber.UnlocksAt(chapter)));
        Assert.Equal((false, (DateTime?)null), (subscriber.IsLockedForViewer(chapter), subscriber.UnlocksAtForViewer(chapter)));
    }

    [Theory]
    [InlineData("100", true)]
    [InlineData("2000", true)]
    [InlineData("150.00", true)]
    [InlineData("150.5", false)]
    [InlineData("1999.99", false)]
    [InlineData("99", false)]
    [InlineData("2001", false)]
    [InlineData("0", false)]
    public void A_price_is_whole_points_from_100_to_2000(string cost, bool valid) =>
        Assert.Equal(valid, EarlyAccess.IsValidCost(decimal.Parse(cost, System.Globalization.CultureInfo.InvariantCulture)));

    [Fact]
    public void Locked_at_is_the_start_of_a_running_lock_only()
    {
        var now = From.AddDays(1);
        var view = new EarlyAccessView(SevenDays, readsLockedChapters: true, now);

        var running = new Chapter { Status = ChapterStatuses.Published, EarlyAccessFrom = DateTime.SpecifyKind(From, DateTimeKind.Unspecified) };
        Assert.Equal(From, view.LockedAt(running));
        Assert.Equal(DateTimeKind.Utc, view.LockedAt(running)!.Value.Kind);

        Assert.Null(view.LockedAt(new Chapter { Status = ChapterStatuses.Published, EarlyAccessFrom = From.AddDays(-8) })); // over
        Assert.Null(view.LockedAt(new Chapter { Status = ChapterStatuses.Published, EarlyAccessFrom = From, EarlyAccessFreedAt = From }));
        Assert.Null(view.LockedAt(new Chapter { Status = ChapterStatuses.Published }));
        Assert.Equal(From, new EarlyAccessView(SubscribersOnly, false, From.AddYears(1)).LockedAt(running));
    }
}
