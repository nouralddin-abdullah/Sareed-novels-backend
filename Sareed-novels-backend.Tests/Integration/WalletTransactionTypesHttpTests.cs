using System.Net;
using Domain.Constants;
using Domain.Entities;

namespace Sareed_novels_backend.Tests.Integration;

/// <summary>
/// The types filter of GET /api/wallet/transactions (#92), for the app's earnings page: only those types, newest first,
/// with paging and a totalCount of those types only, in SQL; known names in any letter case, unknown ones ignored, and a
/// filter naming no known type filtering nothing, as the notifications' (#78). The parsing alone: Unit/TransactionTypeFilterTests.
/// </summary>
[Collection(ReaderApiCollection.Name)]
public class WalletTransactionTypesHttpTests(SardApiFactory api)
{
    /// <summary>What the app's earnings page asks for.</summary>
    private const string EarningsPage = "GiftReceived,PrivilegeRevenue,EarningReversed";

    private sealed record Ledger(ApiUser User, Dictionary<string, PointTransaction> ByName);

    private static PointTransaction Row(ApiUser user, string type, decimal amount, DateTime at) => new()
    {
        Id = Guid.NewGuid(),
        UserId = user.Id,
        Type = type,
        Amount = amount,
        BalanceBefore = 0,
        BalanceAfter = amount,
        Description = "قيد",
        AvailableAt = TransactionType.IsEarning(type) ? at : null,
        CreatedAt = at
    };

    /// <summary>
    /// Ten ledger rows, t1 the oldest, a minute apart, except t6 to t8, which one refund wrote at the same instant. Of the
    /// earnings page's types: t3, t4, t6, t7, t8 and t10. The first week's name: t1.
    /// </summary>
    private async Task<Ledger> SeedLedger()
    {
        var user = await api.SignUp();
        var start = DateTime.UtcNow.AddHours(-1);
        (string Type, decimal Amount, int Minute)[] rows =
        [
            (TransactionType.Recharge, 1000, 0), // t1
            (TransactionType.GiftSent, -100, 1), // t2
            (TransactionType.GiftReceived, 300, 2), // t3
            (TransactionType.PrivilegeRevenue, 150, 3), // t4
            (TransactionType.PlayPurchase, 500, 4), // t5
            (TransactionType.EarningReversed, -100, 5), // t6
            (TransactionType.EarningReversed, -50, 5), // t7
            (TransactionType.EarningReversed, 30, 5), // t8: points given back to her as a payer
            (TransactionType.WithdrawalApproved, -200, 6), // t9
            (TransactionType.GiftReceived, 200, 7), // t10
        ];
        var ledger = rows.Select(row => Row(user, row.Type, row.Amount, start.AddMinutes(row.Minute))).ToList();

        await using var db = api.Db();
        db.PointTransactions.AddRange(ledger);
        await db.SaveChangesAsync();
        return new Ledger(user, ledger.Select((t, i) => ($"t{i + 1}", t)).ToDictionary(p => p.Item1, p => p.t));
    }

    private sealed record Page(List<Guid> Ids, List<string> Types, int TotalCount);

    private async Task<Page> List(ApiUser user, string query)
    {
        var body = await (await api.Get($"/api/wallet/transactions?{query}", user)).OkJson();
        var items = body.GetProperty("transactions").EnumerateArray().ToList();
        return new Page(items.Select(t => t.GetProperty("id").GetGuid()).ToList(), items.Select(t => t.GetProperty("type").GetString()!).ToList(),
            body.GetProperty("totalCount").GetInt32());
    }

    private static HashSet<Guid> Ids(Ledger ledger, params string[] names) => names.Select(name => ledger.ByName[name].Id).ToHashSet();

    /// <summary>The rows of the page, newest first; rows of the same instant in any order, as long as it holds.</summary>
    private static void AssertNewestFirst(Ledger ledger, IReadOnlyList<Guid> ids)
    {
        var at = ids.Select(id => ledger.ByName.Values.Single(t => t.Id == id).CreatedAt).ToList();
        Assert.Equal(at.OrderDescending(), at);
    }

    [Fact]
    public async Task The_filter_lists_only_those_types_newest_first_and_counts_them()
    {
        var ledger = await SeedLedger();

        var earnings = await List(ledger.User, $"types={EarningsPage}");
        Assert.Equal(6, earnings.TotalCount);
        Assert.Equal(Ids(ledger, "t3", "t4", "t6", "t7", "t8", "t10"), earnings.Ids.ToHashSet());
        Assert.Equal(6, earnings.Ids.Count);
        AssertNewestFirst(ledger, earnings.Ids);
        Assert.Equal(ledger.ByName["t10"].Id, earnings.Ids[0]);
        Assert.Equal(ledger.ByName["t3"].Id, earnings.Ids[^1]);

        // One type alone, and the first week's name.
        var gifts = await List(ledger.User, "types=GiftReceived");
        Assert.Equal([ledger.ByName["t10"].Id, ledger.ByName["t3"].Id], gifts.Ids);
        Assert.Equal(2, gifts.TotalCount);
        var firstWeek = await List(ledger.User, "types=Recharge");
        Assert.Equal([ledger.ByName["t1"].Id], firstWeek.Ids);
        Assert.Equal([TransactionType.Recharge], firstWeek.Types);
        Assert.Equal(1, firstWeek.TotalCount);
        var none = await List(ledger.User, "types=BalanceForfeited");
        Assert.Empty(none.Ids);
        Assert.Equal(0, none.TotalCount);

        // Without it, as before: every row.
        var every = await List(ledger.User, "");
        Assert.Equal(10, every.TotalCount);
        Assert.Equal(ledger.ByName.Values.Select(t => t.Id).ToHashSet(), every.Ids.ToHashSet());
        AssertNewestFirst(ledger, every.Ids);
    }

    [Fact]
    public async Task Pages_of_a_filtered_list_hold_each_of_its_rows_once_rows_of_the_same_instant_included()
    {
        var ledger = await SeedLedger();
        var whole = (await List(ledger.User, $"types={EarningsPage}&pageSize=50")).Ids;

        var first = await List(ledger.User, $"types={EarningsPage}&pageSize=4");
        var second = await List(ledger.User, $"types={EarningsPage}&pageSize=4&pageNumber=2");
        var past = await List(ledger.User, $"types={EarningsPage}&pageSize=4&pageNumber=3");
        Assert.Equal((4, 2, 0), (first.Ids.Count, second.Ids.Count, past.Ids.Count));
        Assert.Equal((6, 6, 6), (first.TotalCount, second.TotalCount, past.TotalCount));
        Assert.Equal(whole, first.Ids.Concat(second.Ids));

        // A page of one, all the way down, the refund's three rows of the same instant included: each row once, in order.
        var oneByOne = new List<Guid>();
        for (var page = 1; page <= 6; page++)
        {
            var single = await List(ledger.User, $"types={EarningsPage}&pageSize=1&pageNumber={page}");
            Assert.Equal(6, single.TotalCount);
            oneByOne.AddRange(single.Ids);
        }
        Assert.Equal(whole, oneByOne);
        Assert.Equal(6, oneByOne.Distinct().Count());

        // Every type, paged the same way.
        var all = (await List(ledger.User, "pageSize=50")).Ids;
        var allOneByOne = new List<Guid>();
        for (var page = 1; page <= 10; page++)
        {
            allOneByOne.AddRange((await List(ledger.User, $"pageSize=1&pageNumber={page}")).Ids);
        }
        Assert.Equal(all, allOneByOne);
    }

    [Fact]
    public async Task Unknown_names_are_ignored_and_naming_no_known_type_means_every_type()
    {
        var ledger = await SeedLedger();

        var gifts = await List(ledger.User, "types=GiftReceived,NotAType");
        Assert.Equal(Ids(ledger, "t3", "t10"), gifts.Ids.ToHashSet());
        Assert.Equal(2, gifts.TotalCount);

        foreach (var types in new[] { "NotAType", "UnlockChapter,Gift", "", Uri.EscapeDataString(" , ,"), Uri.EscapeDataString("Gift Received") })
        {
            Assert.Equal(10, (await List(ledger.User, $"types={types}")).TotalCount);
        }
    }

    [Fact]
    public async Task Names_match_in_any_letter_case_with_spaces_around_them_and_the_parameter_may_repeat()
    {
        var ledger = await SeedLedger();
        var expected = Ids(ledger, "t3", "t4", "t10");

        var page = await List(ledger.User, $"types={Uri.EscapeDataString(" giftreceived , PRIVILEGEREVENUE ")}");
        Assert.Equal(expected, page.Ids.ToHashSet());
        Assert.Equal(3, page.TotalCount);
        Assert.All(page.Types, type => Assert.Contains(type, new[] { TransactionType.GiftReceived, TransactionType.PrivilegeRevenue }));

        Assert.Equal(expected, (await List(ledger.User, "types=GiftReceived&types=privilegeRevenue")).Ids.ToHashSet());
    }

    [Fact]
    public async Task Another_members_rows_are_never_listed()
    {
        var (mine, theirs) = (await SeedLedger(), await SeedLedger());

        var page = await List(mine.User, $"types={EarningsPage}&pageSize=50");
        Assert.Equal(Ids(mine, "t3", "t4", "t6", "t7", "t8", "t10"), page.Ids.ToHashSet());
        Assert.Empty(page.Ids.Intersect(theirs.ByName.Values.Select(t => t.Id)));
    }

    [Fact]
    public async Task Signed_out_it_is_401_with_or_without_the_filter()
    {
        Assert.Equal(HttpStatusCode.Unauthorized, (await api.Get("/api/wallet/transactions")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await api.Get($"/api/wallet/transactions?types={EarningsPage}")).StatusCode);
    }
}
