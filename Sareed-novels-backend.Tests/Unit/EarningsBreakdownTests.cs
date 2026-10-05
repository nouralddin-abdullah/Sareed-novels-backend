using System.Globalization;
using Application.Wallet;
using Domain.Constants;
using Domain.Repositories;

namespace Sareed_novels_backend.Tests.Unit;

/// <summary>
/// An author's earnings summary (#78), ledger rows in, sums out: the novels' and the months' sums and their signs, the 12
/// months in UTC, the order of the novels, and that both add up to what was earned. Which rows are earnings:
/// EarningsTests. The same through the API and SQL Server: Integration/EarningsSummaryHttpTests and
/// EarningsSummaryQueryTests.
/// </summary>
public class EarningsBreakdownTests
{
    private static readonly DateTime Now = new(2026, 10, 5, 9, 30, 0, DateTimeKind.Utc);
    private static readonly Guid NovelA = Guid.Parse("10000000-0000-0000-0000-000000000000");
    private static readonly Guid NovelB = Guid.Parse("20000000-0000-0000-0000-000000000000");

    /// <summary>A ledger row of the author's, as far as her earnings are concerned.</summary>
    private sealed record Row(string Type, decimal Amount, Guid? NovelId, DateTime CreatedAt);

    private static Row Gift(decimal amount, Guid? novel, DateTime at) => new(TransactionType.GiftReceived, amount, novel, at);
    private static Row Privilege(decimal amount, Guid? novel, DateTime at) => new(TransactionType.PrivilegeRevenue, amount, novel, at);

    /// <summary>Her side of a reversal: negative, with the novel of the earning it took back.</summary>
    private static Row Reversal(decimal amount, Guid? novel, DateTime at) => new(TransactionType.EarningReversed, -amount, novel, at);

    /// <summary>The other side of a reversal: her own payment given back to her (she paid for another author's earning).</summary>
    private static Row PaymentReturned(decimal amount, Guid? novel, DateTime at) => new(TransactionType.EarningReversed, amount, novel, at);

    /// <summary>The rows summed as the repository's SQL sums them: by novel, month, type and sign.</summary>
    private static List<EarningsGroup> Groups(IEnumerable<Row> rows) => rows
        .GroupBy(r => (r.NovelId, r.CreatedAt.Year, r.CreatedAt.Month, r.Type, Debit: r.Amount < 0))
        .Select(g => new EarningsGroup(g.Key.NovelId, g.Key.Year, g.Key.Month, g.Key.Type, g.Sum(r => r.Amount)))
        .ToList();

    private static List<LedgerEntry> Ledger(IEnumerable<Row> rows) =>
        rows.Select(r => new LedgerEntry(Guid.NewGuid(), r.Type, r.Amount, 0, 0, r.CreatedAt)).ToList();

    private static void AssertSums(EarningsSums sums, decimal gifts, decimal privileges, decimal reversed, decimal total)
    {
        Assert.Equal((gifts, privileges, reversed), (sums.Gifts, sums.Privileges, sums.Reversed));
        Assert.Equal(total, sums.Total);
    }

    // ===== Sums =====

    [Fact]
    public void Reversed_is_positive_and_taken_off_the_total()
    {
        var sums = EarningsSums.Zero
            .Add(EarningKind.Gift, 300)
            .Add(EarningKind.Gift, 200)
            .Add(EarningKind.Privilege, 150)
            .Add(EarningKind.Reversed, -120); // as the ledger has it

        AssertSums(sums, gifts: 500, privileges: 150, reversed: 120, total: 530);
        AssertSums(EarningsSums.Zero, 0, 0, 0, 0);
    }

    // ===== By novel =====

    [Fact]
    public void Each_novel_sums_its_earnings_and_the_reversals_of_them_highest_total_first()
    {
        var rows = new[]
        {
            Gift(300, NovelA, Now.AddDays(-40)), Gift(100, NovelA, Now.AddDays(-2)), Privilege(150, NovelA, Now.AddDays(-1)),
            Reversal(100, NovelA, Now.AddHours(-1)),
            Gift(1000, NovelB, Now.AddDays(-5)), Privilege(150, NovelB, Now.AddDays(-5)),
            Gift(50, null, Now.AddDays(-200)), // from before #17: no novel
            PaymentReturned(70, NovelB, Now.AddHours(-2)) // not hers
        };

        var breakdown = EarningsBreakdown.From(Groups(rows), Now);

        Assert.Equal([NovelB, NovelA, (Guid?)null], breakdown.ByNovel.Select(n => n.NovelId));
        AssertSums(breakdown.ByNovel[0].Sums, gifts: 1000, privileges: 150, reversed: 0, total: 1150);
        AssertSums(breakdown.ByNovel[1].Sums, gifts: 400, privileges: 150, reversed: 100, total: 450);
        AssertSums(breakdown.ByNovel[2].Sums, gifts: 50, privileges: 0, reversed: 0, total: 50);
    }

    [Fact]
    public void A_novel_whose_earnings_were_all_taken_back_stays_with_a_total_of_zero()
    {
        var breakdown = EarningsBreakdown.From(Groups([Gift(300, NovelA, Now.AddDays(-2)), Reversal(300, NovelA, Now.AddDays(-1))]), Now);

        var novel = Assert.Single(breakdown.ByNovel);
        Assert.Equal(NovelA, novel.NovelId);
        AssertSums(novel.Sums, gifts: 300, privileges: 0, reversed: 300, total: 0);
    }

    [Fact]
    public void Equal_totals_keep_one_order_by_novel_id_with_the_entry_without_a_novel_after_the_novels()
    {
        var at = Now.AddDays(-1);
        var rows = new[] { Gift(100, null, at), Gift(100, NovelB, at), Gift(100, NovelA, at), Gift(500, null, at.AddDays(-400)) };

        // Whatever order the rows come in.
        foreach (var order in new[] { rows, rows.Reverse().ToArray() })
        {
            var breakdown = EarningsBreakdown.From(Groups(order), Now);
            Assert.Equal([null, NovelA, NovelB], breakdown.ByNovel.Select(n => n.NovelId));
            Assert.Equal([600m, 100m, 100m], breakdown.ByNovel.Select(n => n.Sums.Total));
        }

        var tied = EarningsBreakdown.From(Groups(rows[..3]), Now);
        Assert.Equal([NovelA, NovelB, null], tied.ByNovel.Select(n => n.NovelId));
    }

    [Fact]
    public void Nothing_earned_means_no_novels_and_twelve_empty_months()
    {
        var breakdown = EarningsBreakdown.From([], Now);

        Assert.Empty(breakdown.ByNovel);
        Assert.Equal(12, breakdown.ByMonth.Count);
        Assert.All(breakdown.ByMonth, m => AssertSums(m.Sums, 0, 0, 0, 0));

        // A payer's returned payment alone is no earning either.
        var payerOnly = EarningsBreakdown.From(Groups([PaymentReturned(100, NovelA, Now.AddDays(-1))]), Now);
        Assert.Empty(payerOnly.ByNovel);
        Assert.All(payerOnly.ByMonth, m => AssertSums(m.Sums, 0, 0, 0, 0));
    }

    // ===== By month =====

    [Fact]
    public void Twelve_calendar_months_in_utc_oldest_first_and_the_current_one_last()
    {
        var breakdown = EarningsBreakdown.From([], Now);

        Assert.Equal(
            Enumerable.Range(0, 12).Select(i => new DateTime(2025, 11, 1, 0, 0, 0, DateTimeKind.Utc).AddMonths(i)),
            breakdown.ByMonth.Select(m => m.Month));
        Assert.All(breakdown.ByMonth, m => Assert.Equal(DateTimeKind.Utc, m.Month.Kind));
        Assert.Equal(new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc), breakdown.ByMonth[^1].Month);
    }

    [Theory]
    [InlineData("2026-01-01T00:00:00", "2025-02-01")]
    [InlineData("2026-01-31T23:59:59.9999999", "2025-02-01")]
    [InlineData("2026-12-31T23:59:59", "2026-01-01")]
    [InlineData("2028-02-29T12:00:00", "2027-03-01")]
    public void The_months_follow_now_across_a_year(string now, string firstMonth)
    {
        var at = DateTime.SpecifyKind(DateTime.Parse(now, CultureInfo.InvariantCulture), DateTimeKind.Utc);
        var first = DateTime.SpecifyKind(DateTime.Parse(firstMonth, CultureInfo.InvariantCulture), DateTimeKind.Utc);

        var months = EarningsBreakdown.From([], at).ByMonth;

        Assert.Equal(first, months[0].Month);
        Assert.Equal(new DateTime(at.Year, at.Month, 1, 0, 0, 0, DateTimeKind.Utc), months[^1].Month);
        Assert.Equal(12, months.Select(m => m.Month).Distinct().Count());
    }

    [Fact]
    public void Each_month_sums_its_own_rows_a_reversal_in_the_month_it_happened_and_months_without_any_are_zero()
    {
        var rows = new[]
        {
            Gift(300, NovelA, new DateTime(2026, 8, 31, 23, 59, 59, DateTimeKind.Utc)),
            Privilege(150, NovelB, new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc)),
            Gift(200, NovelB, new DateTime(2026, 9, 20, 10, 0, 0, DateTimeKind.Utc)),
            // Takes back part of August's gift, in October: October's total goes below zero.
            Reversal(250, NovelA, new DateTime(2026, 10, 2, 8, 0, 0, DateTimeKind.Utc)),
            Gift(40, null, new DateTime(2025, 11, 1, 0, 0, 0, DateTimeKind.Utc)), // the oldest month shown
            Gift(1000, NovelA, new DateTime(2025, 10, 31, 23, 59, 59, DateTimeKind.Utc)), // a month too old
            PaymentReturned(500, NovelB, new DateTime(2026, 9, 3, 0, 0, 0, DateTimeKind.Utc)) // not hers
        };

        var months = EarningsBreakdown.From(Groups(rows), Now).ByMonth.ToDictionary(m => m.Month.ToString("yyyy-MM", CultureInfo.InvariantCulture));

        Assert.Equal(12, months.Count);
        AssertSums(months["2026-08"].Sums, gifts: 300, privileges: 0, reversed: 0, total: 300);
        AssertSums(months["2026-09"].Sums, gifts: 200, privileges: 150, reversed: 0, total: 350);
        AssertSums(months["2026-10"].Sums, gifts: 0, privileges: 0, reversed: 250, total: -250);
        AssertSums(months["2025-11"].Sums, gifts: 40, privileges: 0, reversed: 0, total: 40);
        Assert.False(months.ContainsKey("2025-10"));
        foreach (var empty in months.Keys.Except(["2026-08", "2026-09", "2026-10", "2025-11"]))
        {
            AssertSums(months[empty].Sums, 0, 0, 0, 0);
        }
    }

    [Fact]
    public void Rows_after_now_are_in_no_month_shown()
    {
        var breakdown = EarningsBreakdown.From(Groups([Gift(100, NovelA, Now.AddMonths(1))]), Now);

        Assert.All(breakdown.ByMonth, m => AssertSums(m.Sums, 0, 0, 0, 0));
        Assert.Equal(100m, Assert.Single(breakdown.ByNovel).Sums.Total);
    }

    // ===== Both add up to the same =====

    [Fact]
    public void Over_months_holding_every_row_the_novels_and_the_months_add_up_to_what_was_earned()
    {
        var random = new Random(78);
        var novels = new Guid?[] { NovelA, NovelB, Guid.NewGuid(), null };
        var rows = new List<Row>();
        for (var i = 0; i < 300; i++)
        {
            var at = Now.AddMinutes(-random.Next(0, 330 * 24 * 60)); // within the last 11 months
            var novel = novels[random.Next(novels.Length)];
            var amount = random.Next(1, 50) * 10m;
            rows.Add(random.Next(5) switch
            {
                0 => Privilege(amount, novel, at),
                1 => Reversal(amount / 2, novel, at),
                2 => PaymentReturned(amount, novel, at),
                _ => Gift(amount, novel, at)
            });
        }

        var breakdown = EarningsBreakdown.From(Groups(rows), Now);
        var earned = Earnings.Net(Ledger(rows));

        Assert.Equal(earned, breakdown.ByNovel.Sum(n => n.Sums.Total));
        Assert.Equal(earned, breakdown.ByMonth.Sum(m => m.Sums.Total));
        foreach (var part in new Func<EarningsSums, decimal>[] { s => s.Gifts, s => s.Privileges, s => s.Reversed })
        {
            Assert.Equal(breakdown.ByNovel.Sum(n => part(n.Sums)), breakdown.ByMonth.Sum(m => part(m.Sums)));
        }
        Assert.Equal(rows.Where(r => r.Type == TransactionType.GiftReceived).Sum(r => r.Amount), breakdown.ByNovel.Sum(n => n.Sums.Gifts));
        Assert.Equal(rows.Where(r => r.Type == TransactionType.EarningReversed && r.Amount < 0).Sum(r => -r.Amount),
            breakdown.ByMonth.Sum(m => m.Sums.Reversed));

        // A row older than the months shown counts for its novel only.
        rows.Add(Gift(999, NovelA, Now.AddMonths(-13)));
        var withOld = EarningsBreakdown.From(Groups(rows), Now);
        Assert.Equal(earned + 999, withOld.ByNovel.Sum(n => n.Sums.Total));
        Assert.Equal(earned, withOld.ByMonth.Sum(m => m.Sums.Total));
    }
}
