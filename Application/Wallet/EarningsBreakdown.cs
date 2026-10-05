using Domain.Repositories;

namespace Application.Wallet;

/// <summary>
/// An author's earnings of one novel or one month (#78), in points. <see cref="Gifts"/>, <see cref="Privileges"/> and
/// <see cref="Reversed"/> are never negative; <see cref="Total"/> is Gifts + Privileges − Reversed.
/// </summary>
public sealed record EarningsSums(decimal Gifts, decimal Privileges, decimal Reversed)
{
    public static readonly EarningsSums Zero = new(0, 0, 0);

    /// <summary>
    /// What was earned less what was taken back. Below zero only for a month whose reversals took back more than it
    /// earned (they took back earnings of the months before).
    /// </summary>
    public decimal Total => Gifts + Privileges - Reversed;

    /// <summary>These sums with <paramref name="amount"/> (a row's signed amount, or a sum of such rows) of <paramref name="kind"/> added.</summary>
    public EarningsSums Add(EarningKind kind, decimal amount) => kind switch
    {
        EarningKind.Gift => this with { Gifts = Gifts + amount },
        EarningKind.Privilege => this with { Privileges = Privileges + amount },
        _ => this with { Reversed = Reversed - amount } // negative on the ledger, positive here
    };
}

/// <summary>An author's earnings from one novel; <see cref="NovelId"/> is null for those whose rows don't say which.</summary>
public sealed record NovelEarnings(Guid? NovelId, EarningsSums Sums);

/// <summary>An author's earnings in one calendar month (UTC), <see cref="Month"/> being its first instant.</summary>
public sealed record MonthEarnings(DateTime Month, EarningsSums Sums);

/// <summary>
/// An author's earnings by novel and by month (#78), from her ledger rows summed by novel, month, type and sign
/// (<see cref="IPointTransactionRepository.GetEarningsGroupsAsync"/>), each counted by <see cref="Earnings.KindOf"/>.
/// Both come from the same rows: over months that hold all of them, the novels' totals and the months' totals add up to
/// the same, which is <see cref="Earnings.Net"/> of those rows.
/// </summary>
public sealed class EarningsBreakdown
{
    /// <summary>How many months <see cref="ByMonth"/> covers, the current one included.</summary>
    public const int Months = 12;

    private EarningsBreakdown(IReadOnlyList<NovelEarnings> byNovel, IReadOnlyList<MonthEarnings> byMonth)
    {
        ByNovel = byNovel;
        ByMonth = byMonth;
    }

    /// <summary>
    /// Every novel she has earnings from (all time), one entry for those whose rows don't say which (NovelId null), by
    /// total, highest first; equal totals by NovelId, with the entry without a novel after the novels.
    /// </summary>
    public IReadOnlyList<NovelEarnings> ByNovel { get; }

    /// <summary>The last <see cref="Months"/> calendar months in UTC, the current one last, months without earnings included.</summary>
    public IReadOnlyList<MonthEarnings> ByMonth { get; }

    /// <param name="groups">Her rows as <see cref="IPointTransactionRepository.GetEarningsGroupsAsync"/> sums them.</param>
    /// <param name="now">The moment (UTC) whose month is the last of <see cref="ByMonth"/>.</param>
    public static EarningsBreakdown From(IEnumerable<EarningsGroup> groups, DateTime now)
    {
        var counted = groups
            .Select(g => (Group: g, Kind: Earnings.KindOf(g.Type, g.Amount)))
            .Where(c => c.Kind is not null)
            .ToList();

        var byNovel = counted
            .GroupBy(c => c.Group.NovelId)
            .Select(novel => new NovelEarnings(novel.Key, Sum(novel)))
            .OrderByDescending(n => n.Sums.Total)
            .ThenBy(n => n.NovelId is null)
            .ThenBy(n => n.NovelId)
            .ToList();

        var thisMonth = new DateTime(now.Year, now.Month, 1, 0, 0, 0, DateTimeKind.Utc);
        var months = counted.ToLookup(c => (c.Group.Year, c.Group.Month));
        var byMonth = Enumerable.Range(0, Months)
            .Select(i => thisMonth.AddMonths(i - (Months - 1)))
            .Select(month => new MonthEarnings(month, Sum(months[(month.Year, month.Month)])))
            .ToList();

        return new EarningsBreakdown(byNovel, byMonth);
    }

    private static EarningsSums Sum(IEnumerable<(EarningsGroup Group, EarningKind? Kind)> counted) =>
        counted.Aggregate(EarningsSums.Zero, (sums, c) => sums.Add(c.Kind!.Value, c.Group.Amount));
}
