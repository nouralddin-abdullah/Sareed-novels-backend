using Application.Users;
using Application.Wallet.DTOs;
using Application.Wallet.Queries.GetMyEarnings;
using Domain.Constants;
using Domain.Entities;
using Infrastructure.Repositories;
using NSubstitute;

namespace Sareed_novels_backend.Tests.Integration;

/// <summary>
/// The earnings summary's handler (#78) on SQL Server with a clock the tests set: months are calendar months in UTC to
/// the tick, as SQL groups them, and the summary is the same few queries however many novels and rows it covers. Through
/// the API: EarningsSummaryHttpTests.
/// </summary>
public class EarningsSummaryQueryTests(SqlServerDatabase database) : IClassFixture<SqlServerDatabase>
{
    private static IUserContext SignedIn(User user)
    {
        var context = Substitute.For<IUserContext>();
        context.GetCurrentUser().Returns(new CurrentUser(user.Id, user.Email!, user.UserName!, user.DisplayName));
        return context;
    }

    private async Task<EarningsSummaryDto> Summary(User user, DateTime now, CommandLog? log = null)
    {
        await using var db = log is null ? database.CreateContext() : database.CreateContext(log);
        var handler = new GetMyEarningsQueryHandler(SignedIn(user), WalletTesting.Wallet(db, new MutableClock(now)),
            new PointTransactionRepository(db), new NovelsRepository(db));
        return await handler.Handle(new GetMyEarningsQuery(), CancellationToken.None);
    }

    private async Task<(User Author, List<Novel> Novels)> SeedAuthor(int novels)
    {
        var author = Seed.User();
        var list = Enumerable.Range(0, novels).Select(i => Seed.Novel(author, $"رواية {i} {Seed.Marker()}")).ToList();
        await using var db = database.CreateContext();
        db.Users.Add(author);
        db.Novels.AddRange(list);
        await db.SaveChangesAsync();
        return (author, list);
    }

    private static PointTransaction Row(User user, string type, decimal amount, DateTime at, Guid? novelId, Guid? relatedRequestId = null,
        Guid? reversedTransactionId = null) => new()
    {
        Id = Guid.NewGuid(),
        UserId = user.Id,
        Type = type,
        Amount = amount,
        BalanceBefore = 0,
        BalanceAfter = amount,
        Description = type,
        NovelId = novelId,
        RelatedRequestId = relatedRequestId,
        ReversedTransactionId = reversedTransactionId,
        AvailableAt = TransactionType.IsEarning(type) ? at : null,
        CreatedAt = at
    };

    private async Task Insert(IEnumerable<PointTransaction> rows, params User[] users)
    {
        await using var db = database.CreateContext();
        db.Users.AddRange(users);
        db.PointTransactions.AddRange(rows);
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task Months_are_calendar_months_in_utc_to_the_tick()
    {
        var (author, novels) = await SeedAuthor(1);
        var novel = novels[0].Id;
        var now = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        await Insert(
        [
            Row(author, TransactionType.GiftReceived, 1, now, novel), // the current month's first tick
            Row(author, TransactionType.GiftReceived, 2, now.AddTicks(-1), novel), // last year's last tick
            Row(author, TransactionType.PrivilegeRevenue, 4, new DateTime(2025, 2, 1, 0, 0, 0, DateTimeKind.Utc), novel), // the first month shown
            Row(author, TransactionType.GiftReceived, 8, new DateTime(2025, 2, 1, 0, 0, 0, DateTimeKind.Utc).AddTicks(-1), novel) // one tick too old
        ]);

        var summary = await Summary(author, now);

        Assert.Equal(
            ["2025-02", "2025-03", "2025-04", "2025-05", "2025-06", "2025-07", "2025-08", "2025-09", "2025-10", "2025-11", "2025-12", "2026-01"],
            summary.ByMonth.Select(m => m.Month));
        Assert.Equal((1m, 1m), (summary.ByMonth[11].Gifts, summary.ByMonth[11].Total));
        Assert.Equal((2m, 2m), (summary.ByMonth[10].Gifts, summary.ByMonth[10].Total));
        Assert.Equal((0m, 4m, 4m), (summary.ByMonth[0].Gifts, summary.ByMonth[0].Privileges, summary.ByMonth[0].Total));
        Assert.Equal(7m, summary.ByMonth.Sum(m => m.Total));
        Assert.Equal(15m, Assert.Single(summary.ByNovel).Total);
        Assert.Equal(15m, summary.TotalEarned);

        // A moment later, still January: the same months.
        Assert.Equal(summary.ByMonth.Select(m => m.Month), (await Summary(author, now.AddDays(30).AddHours(23))).ByMonth.Select(m => m.Month));
    }

    [Fact]
    public async Task The_summary_is_the_same_six_queries_however_many_novels_and_rows()
    {
        var at = new DateTime(2026, 9, 10, 12, 0, 0, DateTimeKind.Utc);
        var now = at.AddDays(20);

        // One novel, one gift.
        var (small, smallNovels) = await SeedAuthor(1);
        var reader = Seed.User();
        var gift = Guid.NewGuid();
        await Insert(
        [
            Row(reader, TransactionType.GiftSent, -100, at, smallNovels[0].Id, gift),
            Row(small, TransactionType.GiftReceived, 100, at, smallNovels[0].Id, gift)
        ], reader);

        // Six novels, each with gifts and subscriptions from several readers over months, reversals, and old rows.
        var (large, largeNovels) = await SeedAuthor(6);
        var readers = Enumerable.Range(0, 5).Select(_ => Seed.User()).ToList();
        var rows = new List<PointTransaction>();
        for (var i = 0; i < 60; i++)
        {
            var (payer, novel, paid, link) = (readers[i % readers.Count], largeNovels[i % largeNovels.Count].Id, at.AddDays(-i * 5), Guid.NewGuid());
            var subscription = i % 4 == 0;
            rows.Add(Row(payer, subscription ? TransactionType.PrivilegeSubscription : TransactionType.GiftSent, -50, paid, novel, link));
            var earning = Row(large, subscription ? TransactionType.PrivilegeRevenue : TransactionType.GiftReceived, 50, paid, novel, link);
            rows.Add(earning);
            if (i % 7 == 0)
            {
                rows.Add(Row(large, TransactionType.EarningReversed, -20, paid.AddDays(1), novel, Guid.NewGuid(), earning.Id));
            }
        }
        rows.Add(Row(large, TransactionType.GiftReceived, 500, at.AddYears(-1), null));
        await Insert(rows, readers.ToArray());

        var smallLog = new CommandLog();
        var smallSummary = await Summary(small, now, smallLog);
        var largeLog = new CommandLog();
        var largeSummary = await Summary(large, now, largeLog);

        // The wallet's figures (its ledger, balance and pending withdrawals), the grouped rows, the supporters and the novels.
        Assert.Equal(6, smallLog.Commands.Count);
        Assert.Equal(6, largeLog.Commands.Count);

        Assert.Equal(100m, smallSummary.TotalEarned);
        Assert.Equal(1, Assert.Single(smallSummary.ByNovel).SupportersCount);
        Assert.Equal(7, largeSummary.ByNovel.Count); // six novels and the old rows without one
        Assert.Equal(60 * 50 + 500 - 9 * 20, largeSummary.TotalEarned);
        Assert.Equal(largeSummary.TotalEarned, largeSummary.ByNovel.Sum(n => n.Total));
        Assert.All(largeSummary.ByNovel.Where(n => n.NovelId is not null), n => Assert.Equal(5, n.SupportersCount));
        Assert.Equal(0, Assert.Single(largeSummary.ByNovel, n => n.NovelId is null).SupportersCount);
    }
}
