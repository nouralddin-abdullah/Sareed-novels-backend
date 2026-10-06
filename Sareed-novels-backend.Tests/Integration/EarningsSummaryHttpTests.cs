using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Application.Services;
using Application.Wallet.Queries.GetMyEarnings;
using Domain.Constants;
using Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Sareed_novels_backend.Tests.Integration;

/// <summary>
/// GET /api/wallet/earnings (#78), writer mode's earnings screen, through the real gift, subscription and refund paths:
/// the sums by novel and by month against the ledger rows they come from, the wallet's own figures, a reversal, rows from
/// before the links, deleted and draft novels, months without earnings, and nobody else's rows. The arithmetic alone:
/// Unit/EarningsTests and Unit/EarningsBreakdownTests.
/// </summary>
[Collection(ReaderApiCollection.Name)]
public class EarningsSummaryHttpTests(SardApiFactory api)
{
    // Production's catalog: a rose costs 100 points.
    private static readonly Guid Rose = Guid.Parse("ec16dfde-71b8-4e23-8ff5-d1846cdf2036");
    private const decimal SubscriptionCost = 150;

    // ===== Reading the summary =====

    /// <summary>An entry of byNovel; a novel neither deleted nor a draft unless said (#92).</summary>
    private sealed record NovelLine(Guid? NovelId, string? Slug, string? Title, string? Cover, decimal Gifts, decimal Privileges,
        decimal Reversed, decimal Total, int Supporters, bool? IsDeleted = false, bool? IsDraft = false);

    private sealed record MonthLine(string Month, decimal Gifts, decimal Privileges, decimal Reversed, decimal Total);

    private sealed record Summary(decimal TotalEarned, decimal PendingEarnings, string? NextReleaseAt, List<NovelLine> ByNovel,
        List<MonthLine> ByMonth)
    {
        public NovelLine Novel(Guid? id) => Assert.Single(ByNovel, n => n.NovelId == id);
        public MonthLine Month(string month) => Assert.Single(ByMonth, m => m.Month == month);
    }

    private static readonly string[] SummaryFields = ["totalEarned", "pendingEarnings", "nextReleaseAt", "byNovel", "byMonth"];

    private static readonly string[] NovelFields =
        ["novelId", "novelSlug", "novelTitle", "coverImageUrl", "isDeleted", "isDraft", "gifts", "privileges", "reversed", "total",
            "supportersCount"];

    private static readonly string[] MonthFields = ["month", "gifts", "privileges", "reversed", "total"];

    private static List<string> Fields(JsonElement element) => element.EnumerateObject().Select(p => p.Name).ToList();

    private static string? NullableString(JsonElement element) => element.ValueKind == JsonValueKind.Null ? null : element.GetString();

    private static bool? NullableBool(JsonElement element) => element.ValueKind == JsonValueKind.Null ? null : element.GetBoolean();

    /// <summary>The summary, checked to have exactly the issue's fields.</summary>
    private async Task<Summary> Earnings(ApiUser user)
    {
        var body = await (await api.Get("/api/wallet/earnings", user)).OkJson();
        Assert.Equal(SummaryFields, Fields(body));
        var byNovel = body.GetProperty("byNovel").EnumerateArray().Select(n =>
        {
            Assert.Equal(NovelFields, Fields(n));
            var id = n.GetProperty("novelId");
            return new NovelLine(id.ValueKind == JsonValueKind.Null ? null : id.GetGuid(), NullableString(n.GetProperty("novelSlug")),
                NullableString(n.GetProperty("novelTitle")), NullableString(n.GetProperty("coverImageUrl")),
                n.GetProperty("gifts").GetDecimal(), n.GetProperty("privileges").GetDecimal(), n.GetProperty("reversed").GetDecimal(),
                n.GetProperty("total").GetDecimal(), n.GetProperty("supportersCount").GetInt32(), NullableBool(n.GetProperty("isDeleted")),
                NullableBool(n.GetProperty("isDraft")));
        }).ToList();
        var byMonth = body.GetProperty("byMonth").EnumerateArray().Select(m =>
        {
            Assert.Equal(MonthFields, Fields(m));
            return new MonthLine(m.GetProperty("month").GetString()!, m.GetProperty("gifts").GetDecimal(),
                m.GetProperty("privileges").GetDecimal(), m.GetProperty("reversed").GetDecimal(), m.GetProperty("total").GetDecimal());
        }).ToList();
        return new Summary(body.GetProperty("totalEarned").GetDecimal(), body.GetProperty("pendingEarnings").GetDecimal(),
            NullableString(body.GetProperty("nextReleaseAt")), byNovel, byMonth);
    }

    private static string MonthOf(DateTime at) => at.ToString("yyyy-MM", CultureInfo.InvariantCulture);

    /// <summary>
    /// The 12 months end with the current one, oldest first, consecutive (the current month read around the request, in
    /// case it turned meanwhile), and nothing but the months holds a total.
    /// </summary>
    private static void AssertTwelveMonths(Summary summary, DateTime before, DateTime after)
    {
        Assert.Equal(12, summary.ByMonth.Count);
        Assert.Contains(summary.ByMonth[^1].Month, new[] { MonthOf(before), MonthOf(after) });
        var last = DateTime.ParseExact(summary.ByMonth[^1].Month, "yyyy-MM", CultureInfo.InvariantCulture);
        Assert.Equal(Enumerable.Range(0, 12).Select(i => MonthOf(last.AddMonths(i - 11))), summary.ByMonth.Select(m => m.Month));
        Assert.All(summary.ByMonth, m => Assert.Equal(m.Gifts + m.Privileges - m.Reversed, m.Total));
        Assert.All(summary.ByNovel, n => Assert.Equal(n.Gifts + n.Privileges - n.Reversed, n.Total));
    }

    /// <summary>
    /// What the user's ledger says she earned, read straight from the rows: gifts and privilege subscriptions received,
    /// less her side of the reversals (negative); with each row's month.
    /// </summary>
    private async Task<List<PointTransaction>> EarningRows(ApiUser user)
    {
        await using var db = api.Db();
        return await db.PointTransactions.AsNoTracking()
            .Where(t => t.UserId == user.Id
                        && (t.Type == TransactionType.GiftReceived || t.Type == TransactionType.PrivilegeRevenue
                            || (t.Type == TransactionType.EarningReversed && t.Amount < 0)))
            .ToListAsync();
    }

    /// <summary>
    /// The summary adds up: its novels' totals to totalEarned and to her ledger rows, totalEarned, pendingEarnings and
    /// nextReleaseAt are GET /api/wallet's, and, when every row is in the months shown, the months add up to the same.
    /// </summary>
    private async Task AssertAddsUp(ApiUser user, Summary summary, bool allInTheMonthsShown = true)
    {
        var rows = await EarningRows(user);
        var earned = rows.Sum(t => t.Amount);
        Assert.Equal(earned, summary.TotalEarned);
        Assert.Equal(earned, summary.ByNovel.Sum(n => n.Total));
        Assert.Equal(rows.Where(t => t.Type == TransactionType.GiftReceived).Sum(t => t.Amount), summary.ByNovel.Sum(n => n.Gifts));
        Assert.Equal(rows.Where(t => t.Type == TransactionType.PrivilegeRevenue).Sum(t => t.Amount), summary.ByNovel.Sum(n => n.Privileges));
        Assert.Equal(-rows.Where(t => t.Type == TransactionType.EarningReversed).Sum(t => t.Amount), summary.ByNovel.Sum(n => n.Reversed));
        if (allInTheMonthsShown)
        {
            Assert.Equal(earned, summary.ByMonth.Sum(m => m.Total));
            Assert.Equal(summary.ByNovel.Sum(n => n.Gifts), summary.ByMonth.Sum(m => m.Gifts));
            Assert.Equal(summary.ByNovel.Sum(n => n.Privileges), summary.ByMonth.Sum(m => m.Privileges));
            Assert.Equal(summary.ByNovel.Sum(n => n.Reversed), summary.ByMonth.Sum(m => m.Reversed));
            foreach (var month in rows.GroupBy(t => MonthOf(t.CreatedAt)))
            {
                Assert.Equal(month.Sum(t => t.Amount), summary.Month(month.Key).Total);
            }
        }

        var wallet = await (await api.Get("/api/wallet", user)).OkJson();
        Assert.Equal(summary.TotalEarned, wallet.GetProperty("totalEarned").GetDecimal());
        Assert.Equal(summary.PendingEarnings, wallet.GetProperty("pendingEarnings").GetDecimal());
        Assert.Equal(summary.NextReleaseAt, NullableString(wallet.GetProperty("nextReleaseAt")));
    }

    // ===== Acting through the API =====

    private async Task Fund(ApiUser user, decimal balance)
    {
        await using var db = api.Db();
        db.UserWallets.Add(new UserWallet { Id = Guid.NewGuid(), UserId = user.Id, CurrentBalance = balance });
        await db.SaveChangesAsync();
    }

    /// <summary>A novel of the author's with a cover of its own, and privilege subscriptions on offer when asked.</summary>
    private async Task<Novel> Novel(ApiUser author, bool privileges = false)
    {
        var novel = await api.AddNovel(author);
        novel.CoverImageUrl = $"https://files.test/covers/{novel.Id:N}.webp";
        await using var db = api.Db();
        await db.Novels.Where(n => n.Id == novel.Id).ExecuteUpdateAsync(s => s.SetProperty(n => n.CoverImageUrl, novel.CoverImageUrl));
        if (privileges)
        {
            db.NovelPrivileges.Add(new NovelPrivilege
            {
                Id = Guid.NewGuid(), NovelId = novel.Id, IsEnabled = true, SubscriptionCost = SubscriptionCost, CurrentLockedCount = 5,
                PrivilegeStartSequence = 11
            });
            await db.SaveChangesAsync();
        }
        return novel;
    }

    private async Task Gift(ApiUser sender, Novel novel, int roses) =>
        await (await api.Send(HttpMethod.Post, "/api/gift/send", sender,
            JsonContent.Create(new { giftId = Rose, novelId = novel.Id, count = roses }))).OkJson();

    private async Task Subscribe(ApiUser reader, Novel novel) =>
        Assert.True((await (await api.Send(HttpMethod.Post, $"/api/novel/{novel.Id}/privilege/subscribe", reader)).OkJson())
            .GetProperty("success").GetBoolean());

    /// <summary>
    /// Google voids a Play purchase of <paramref name="points"/> by the buyer: the wallet service takes the points back and,
    /// what their balance can't cover, from the earnings on hold they paid for (two EarningReversed rows each).
    /// </summary>
    private async Task Refund(ApiUser buyer, decimal points)
    {
        using var scope = api.Services.CreateScope();
        var wallet = scope.ServiceProvider.GetRequiredService<IWalletService>();
        var wallets = await wallet.PlanPlayRefundAsync(buyer.Id, points);
        var outcome = await wallet.RefundPlayPurchaseAsync(buyer.Id, points, Guid.NewGuid(), "استرداد عملية شراء", wallets);
        Assert.True(outcome.Reversals > 0);
    }

    /// <summary>An earning row as an older version of the API wrote it, straight into the ledger (released at once).</summary>
    private static PointTransaction OldEarning(ApiUser author, string type, decimal amount, DateTime at, Guid? novelId = null) => new()
    {
        Id = Guid.NewGuid(),
        UserId = author.Id,
        Type = type,
        Amount = amount,
        BalanceBefore = 0,
        BalanceAfter = amount,
        Description = type == TransactionType.GiftReceived ? "هدية" : "اشتراك",
        NovelId = novelId, // since #17
        RelatedRequestId = null, // the gift or subscription record, since #22
        AvailableAt = at,
        CreatedAt = at
    };

    private async Task Insert(params PointTransaction[] rows)
    {
        await using var db = api.Db();
        db.PointTransactions.AddRange(rows);
        await db.SaveChangesAsync();
    }

    // ===== The tests =====

    [Fact]
    public async Task Signed_out_it_is_401()
    {
        Assert.Equal(HttpStatusCode.Unauthorized, (await api.Get("/api/wallet/earnings")).StatusCode);
    }

    [Fact]
    public async Task A_member_without_earnings_gets_zeros_and_twelve_empty_months()
    {
        var member = await api.SignUp();

        var before = DateTime.UtcNow;
        var summary = await Earnings(member);
        AssertTwelveMonths(summary, before, DateTime.UtcNow);

        Assert.Equal((0m, 0m, null), (summary.TotalEarned, summary.PendingEarnings, summary.NextReleaseAt));
        Assert.Empty(summary.ByNovel);
        Assert.All(summary.ByMonth, m => Assert.Equal((0m, 0m, 0m, 0m), (m.Gifts, m.Privileges, m.Reversed, m.Total)));
        await AssertAddsUp(member, summary);
    }

    [Fact]
    public async Task Gifts_and_privilege_subscriptions_add_up_by_novel_and_by_month_as_the_wallet_says()
    {
        var (author, otherAuthor, first, second) = (await api.SignUp(), await api.SignUp(), await api.SignUp(), await api.SignUp());
        var (withPrivileges, plain, othersNovel) = (await Novel(author, privileges: true), await Novel(author), await Novel(otherAuthor));
        await Fund(first, 2000);
        await Fund(second, 2000);

        await Gift(first, withPrivileges, 3);
        await Gift(first, withPrivileges, 1); // the same supporter again
        await Subscribe(first, withPrivileges);
        await Subscribe(second, withPrivileges);
        await Gift(second, plain, 2);
        await Gift(first, othersNovel, 5); // another author's earnings

        var before = DateTime.UtcNow;
        var summary = await Earnings(author);
        AssertTwelveMonths(summary, before, DateTime.UtcNow);

        Assert.Equal([withPrivileges.Id, plain.Id], summary.ByNovel.Select(n => n.NovelId));
        Assert.Equal(new NovelLine(withPrivileges.Id, withPrivileges.Slug, withPrivileges.Title, withPrivileges.CoverImageUrl,
            Gifts: 400, Privileges: 300, Reversed: 0, Total: 700, Supporters: 2), summary.ByNovel[0]);
        Assert.Equal(new NovelLine(plain.Id, plain.Slug, plain.Title, plain.CoverImageUrl,
            Gifts: 200, Privileges: 0, Reversed: 0, Total: 200, Supporters: 1), summary.ByNovel[1]);

        // Everything was received now: on hold, released in 30 days.
        Assert.Equal((900m, 900m), (summary.TotalEarned, summary.PendingEarnings));
        Assert.NotNull(summary.NextReleaseAt);
        Assert.EndsWith("Z", summary.NextReleaseAt);
        var month = MonthOf((await EarningRows(author)).Max(t => t.CreatedAt));
        Assert.Equal(new MonthLine(month, Gifts: 600, Privileges: 300, Reversed: 0, Total: 900), summary.Month(month));
        Assert.All(summary.ByMonth.Where(m => m.Month != month), m => Assert.Equal(0m, m.Total));
        await AssertAddsUp(author, summary);

        // Nobody else's rows: the other author has only their own novel, and the readers, who paid, earned nothing.
        var others = await Earnings(otherAuthor);
        Assert.Equal(new NovelLine(othersNovel.Id, othersNovel.Slug, othersNovel.Title, othersNovel.CoverImageUrl,
            Gifts: 500, Privileges: 0, Reversed: 0, Total: 500, Supporters: 1), Assert.Single(others.ByNovel));
        await AssertAddsUp(otherAuthor, others);
        foreach (var reader in new[] { first, second })
        {
            var paid = await Earnings(reader);
            Assert.Empty(paid.ByNovel);
            Assert.Equal((0m, 0m), (paid.TotalEarned, paid.ByMonth.Sum(m => m.Total)));
            await AssertAddsUp(reader, paid);
        }
    }

    [Fact]
    public async Task A_refund_takes_back_under_the_novel_of_the_earning_and_a_payers_returned_points_are_no_earnings()
    {
        // The buyer gifts the author's novel 300 with points a refund later takes back. The buyer writes too: a reader
        // gifted their own novel 100.
        var (author, buyer, reader) = (await api.SignUp(), await api.SignUp(), await api.SignUp());
        var (novel, buyersNovel) = (await Novel(author), await Novel(buyer));
        await Fund(buyer, 1000);
        await Fund(reader, 1000);
        await Gift(buyer, novel, 3);
        await Gift(reader, buyersNovel, 1);

        // 1000 refunded from a balance of 800: the 200 it can't cover come back from the author's gift, still on hold.
        await Refund(buyer, 1000);

        var before = DateTime.UtcNow;
        var summary = await Earnings(author);
        AssertTwelveMonths(summary, before, DateTime.UtcNow);
        Assert.Equal(new NovelLine(novel.Id, novel.Slug, novel.Title, novel.CoverImageUrl,
            Gifts: 300, Privileges: 0, Reversed: 200, Total: 100, Supporters: 1), Assert.Single(summary.ByNovel));
        Assert.Equal((100m, 100m), (summary.TotalEarned, summary.PendingEarnings));
        var reversal = Assert.Single(await EarningRows(author), t => t.Type == TransactionType.EarningReversed);
        Assert.Equal(-200m, reversal.Amount);
        var month = summary.Month(MonthOf(reversal.CreatedAt));
        Assert.Equal(200m, month.Reversed);
        await AssertAddsUp(author, summary);

        // The buyer got 200 back (a positive EarningReversed): their own points, not an earning. Only their novel's gift is.
        await using (var db = api.Db())
        {
            Assert.Equal(200m, await db.PointTransactions
                .Where(t => t.UserId == buyer.Id && t.Type == TransactionType.EarningReversed).SumAsync(t => t.Amount));
        }
        var buyers = await Earnings(buyer);
        Assert.Equal(new NovelLine(buyersNovel.Id, buyersNovel.Slug, buyersNovel.Title, buyersNovel.CoverImageUrl,
            Gifts: 100, Privileges: 0, Reversed: 0, Total: 100, Supporters: 1), Assert.Single(buyers.ByNovel));
        Assert.Equal(100m, buyers.TotalEarned);
        Assert.Equal(0m, buyers.ByMonth.Sum(m => m.Reversed));
        await AssertAddsUp(buyer, buyers);
    }

    [Fact]
    public async Task Rows_from_before_the_links_keep_their_novel_when_they_name_it_and_share_one_entry_when_they_dont()
    {
        var (author, reader) = (await api.SignUp(), await api.SignUp());
        var novel = await Novel(author);
        var at = DateTime.UtcNow.AddDays(-3);
        await Fund(author, 850);
        await Insert(
            OldEarning(author, TransactionType.GiftReceived, 500, at), // before #17: no novel, no link
            OldEarning(author, TransactionType.PrivilegeRevenue, 150, at.AddMinutes(1)),
            OldEarning(author, TransactionType.GiftReceived, 200, at.AddMinutes(2), novel.Id)); // #17 to #22: a novel, no link
        await Fund(reader, 1000);
        await Gift(reader, novel, 1); // linked to its payment: a supporter

        var summary = await Earnings(author);

        Assert.Equal(
        [
            new NovelLine(null, null, GetMyEarningsQueryHandler.NoNovelTitle, null, Gifts: 500, Privileges: 150, Reversed: 0, Total: 650,
                Supporters: 0, IsDeleted: null, IsDraft: null),
            new NovelLine(novel.Id, novel.Slug, novel.Title, novel.CoverImageUrl, Gifts: 300, Privileges: 0, Reversed: 0, Total: 300,
                Supporters: 1)
        ], summary.ByNovel);
        Assert.Equal("أرباح بلا رواية محددة", summary.ByNovel[0].Title);
        Assert.Equal(950m, summary.TotalEarned);
        Assert.Equal(100m, summary.PendingEarnings); // the old rows were released at once
        await AssertAddsUp(author, summary);
    }

    [Fact]
    public async Task A_reversal_counts_under_the_novel_of_the_earning_it_took_back_whatever_its_own_row_says()
    {
        // The refund path copies the earning's novel onto its reversal; the summary doesn't rely on that copy.
        var author = await api.SignUp();
        var novel = await Novel(author);
        var at = DateTime.UtcNow.AddDays(-2);
        var earning = OldEarning(author, TransactionType.GiftReceived, 300, at, novel.Id);
        earning.AvailableAt = at.AddDays(30);
        earning.RelatedRequestId = Guid.NewGuid();
        var reversal = new PointTransaction
        {
            Id = Guid.NewGuid(), UserId = author.Id, Type = TransactionType.EarningReversed, Amount = -100, BalanceBefore = 300,
            BalanceAfter = 200, Description = "أُلغيت أرباح 100 نقطة لأن عملية الشراء التي جاءت منها استُرد مبلغها",
            RelatedRequestId = Guid.NewGuid(), ReversedTransactionId = earning.Id, NovelId = null, CreatedAt = at.AddHours(1)
        };
        await Fund(author, 200);
        await Insert(earning, reversal);

        var summary = await Earnings(author);

        Assert.Equal(new NovelLine(novel.Id, novel.Slug, novel.Title, novel.CoverImageUrl, Gifts: 300, Privileges: 0, Reversed: 100,
            Total: 200, Supporters: 0), Assert.Single(summary.ByNovel));
        Assert.Equal((200m, 200m), (summary.TotalEarned, summary.PendingEarnings));
        await AssertAddsUp(author, summary);
    }

    [Fact]
    public async Task Deleted_and_draft_novels_are_listed_by_their_title_and_say_which_they_are()
    {
        var (author, reader) = (await api.SignUp(), await api.SignUp());
        var (deleted, draft, deletedDraft, republished, kept) =
            (await Novel(author), await Novel(author), await Novel(author), await Novel(author), await Novel(author));
        await Fund(reader, 5000);
        await Gift(reader, deleted, 6);
        await Gift(reader, draft, 5);
        await Gift(reader, deletedDraft, 4);
        await Gift(reader, republished, 3);
        await Gift(reader, kept, 1);

        (await api.Send(HttpMethod.Delete, $"/api/myworks/{deleted.Id}/delete", author)).EnsureSuccessStatusCode();
        (await api.Send(HttpMethod.Patch, $"/api/myworks/{draft.Id}/draft", author)).EnsureSuccessStatusCode();
        (await api.Send(HttpMethod.Patch, $"/api/myworks/{deletedDraft.Id}/draft", author)).EnsureSuccessStatusCode();
        (await api.Send(HttpMethod.Delete, $"/api/myworks/{deletedDraft.Id}/delete", author)).EnsureSuccessStatusCode();
        (await api.Send(HttpMethod.Patch, $"/api/myworks/{republished.Id}/draft", author)).EnsureSuccessStatusCode();
        (await api.Send(HttpMethod.Patch, $"/api/myworks/{republished.Id}/publish", author)).EnsureSuccessStatusCode();
        await using (var db = api.Db())
        {
            Assert.True((await db.Novels.IgnoreQueryFilters().SingleAsync(n => n.Id == deleted.Id)).IsDeleted);
            Assert.True((await db.Novels.SingleAsync(n => n.Id == draft.Id)).IsDraft);
            var both = await db.Novels.IgnoreQueryFilters().SingleAsync(n => n.Id == deletedDraft.Id);
            Assert.True(both.IsDraft && both.IsDeleted);
        }

        var summary = await Earnings(author);

        // «محذوفة» or «مخفية», one at most: a deleted draft is deleted.
        Assert.Equal(
        [
            new NovelLine(deleted.Id, deleted.Slug, deleted.Title, deleted.CoverImageUrl, 600, 0, 0, 600, 1, IsDeleted: true),
            new NovelLine(draft.Id, draft.Slug, draft.Title, draft.CoverImageUrl, 500, 0, 0, 500, 1, IsDraft: true),
            new NovelLine(deletedDraft.Id, deletedDraft.Slug, deletedDraft.Title, deletedDraft.CoverImageUrl, 400, 0, 0, 400, 1,
                IsDeleted: true),
            new NovelLine(republished.Id, republished.Slug, republished.Title, republished.CoverImageUrl, 300, 0, 0, 300, 1),
            new NovelLine(kept.Id, kept.Slug, kept.Title, kept.CoverImageUrl, 100, 0, 0, 100, 1)
        ], summary.ByNovel);
        await AssertAddsUp(author, summary);
    }

    [Fact]
    public async Task Months_without_earnings_are_zero_and_rows_older_than_the_twelve_months_count_for_their_novel_only()
    {
        var author = await api.SignUp();
        var (novel, older) = (await Novel(author), await Novel(author));
        var now = DateTime.UtcNow;
        DateTime MidMonth(int monthsAgo) => new DateTime(now.Year, now.Month, 15, 12, 0, 0, DateTimeKind.Utc).AddMonths(-monthsAgo);
        await Fund(author, 2000);
        await Insert(
            OldEarning(author, TransactionType.GiftReceived, 100, MidMonth(1), novel.Id),
            OldEarning(author, TransactionType.PrivilegeRevenue, 150, MidMonth(1).AddDays(2), novel.Id),
            OldEarning(author, TransactionType.GiftReceived, 300, MidMonth(4), novel.Id),
            OldEarning(author, TransactionType.GiftReceived, 250, MidMonth(9), older.Id),
            OldEarning(author, TransactionType.GiftReceived, 1200, MidMonth(14), older.Id)); // too old for the months

        var before = DateTime.UtcNow;
        var summary = await Earnings(author);
        AssertTwelveMonths(summary, before, DateTime.UtcNow);

        Assert.Equal(new MonthLine(MonthOf(MidMonth(1)), 100, 150, 0, 250), summary.Month(MonthOf(MidMonth(1))));
        Assert.Equal(new MonthLine(MonthOf(MidMonth(4)), 300, 0, 0, 300), summary.Month(MonthOf(MidMonth(4))));
        Assert.Equal(new MonthLine(MonthOf(MidMonth(9)), 250, 0, 0, 250), summary.Month(MonthOf(MidMonth(9))));
        var withEarnings = new[] { MidMonth(1), MidMonth(4), MidMonth(9) }.Select(MonthOf).ToList();
        Assert.All(summary.ByMonth.Where(m => !withEarnings.Contains(m.Month)),
            m => Assert.Equal((0m, 0m, 0m, 0m), (m.Gifts, m.Privileges, m.Reversed, m.Total)));
        Assert.DoesNotContain(summary.ByMonth, m => m.Month == MonthOf(MidMonth(14)));

        // The novel's sums are of all time; the months only show the last 12.
        Assert.Equal([older.Id, novel.Id], summary.ByNovel.Select(n => n.NovelId));
        Assert.Equal((1450m, 550m), (summary.Novel(older.Id).Total, summary.Novel(novel.Id).Total));
        Assert.Equal(2000m, summary.TotalEarned);
        Assert.Equal(800m, summary.ByMonth.Sum(m => m.Total));
        Assert.Equal(summary.TotalEarned - 1200m, summary.ByMonth.Sum(m => m.Total));
        await AssertAddsUp(author, summary, allInTheMonthsShown: false);
    }
}
