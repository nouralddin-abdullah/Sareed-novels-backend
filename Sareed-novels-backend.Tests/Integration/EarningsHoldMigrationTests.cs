using System.Reflection;
using Infrastructure.Migrations;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Sareed_novels_backend.Tests.Integration;

/// <summary>
/// <see cref="EarningsHoldAndReversal"/> on a wallet as production has it: earnings from before the hold existed are
/// released at once, nothing else changes, and from then on the database refuses an earning without a release date.
/// </summary>
public class EarningsHoldMigrationTests(EmptySqlServerDatabase database) : IClassFixture<EmptySqlServerDatabase>
{
    private const string Before = "20260928001225_ArabicGiftNamesAndTransactionDetails";
    private static readonly string Hold = typeof(EarningsHoldAndReversal).GetCustomAttribute<MigrationAttribute>()!.Id;

    private sealed record Row(Guid Id, string Type, DateTime CreatedAt, DateTime? AvailableAt, Guid? ReversedTransactionId);

    [Fact]
    public async Task Earnings_from_before_are_released_at_once_and_nothing_else_changes()
    {
        await using var db = database.CreateContext();
        var migrator = db.GetService<IMigrator>();
        await migrator.MigrateAsync(Before);
        Assert.Equal(Before, (await db.Database.GetAppliedMigrationsAsync()).Last());

        var now = DateTime.UtcNow;
        var author = Seed.User("كاتبة");
        await Seed.InsertUserRowAsync(db, author);
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO UserWallets (Id, UserId, CurrentBalance, TotalRecharged, TotalWithdrawn, TotalSpent, TotalEarned, CreatedAt, UpdatedAt)
            VALUES ({Guid.NewGuid()}, {author.Id}, 3700, 0, 0, 0, 0, {now}, {now})
            """);

        // A gift received an hour before the deploy, a privilege subscription a year before, and what isn't an earning.
        var balance = 0m;
        foreach (var (type, amount, createdAt) in new[]
                 {
                     ("PrivilegeRevenue", 700m, now.AddYears(-1)),
                     ("RechargeApproved", 5000m, now.AddDays(-10)),
                     ("GiftSent", -3000m, now.AddDays(-5)),
                     ("GiftReceived", 1000m, now.AddHours(-1)),
                 })
        {
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO PointTransactions (Id, UserId, Type, Amount, BalanceBefore, BalanceAfter, Description, CreatedAt)
                VALUES ({Guid.NewGuid()}, {author.Id}, {type}, {amount}, {balance}, {balance + amount}, N'قيد', {createdAt})
                """);
            balance += amount;
        }

        await migrator.MigrateAsync();
        await AssertReleasedAtOnce();

        // An earning row without a release date is refused from now on.
        var error = await Assert.ThrowsAsync<SqlException>(() => db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO PointTransactions (Id, UserId, Type, Amount, BalanceBefore, BalanceAfter, Description, CreatedAt)
            VALUES ({Guid.NewGuid()}, {author.Id}, N'GiftReceived', 10, 0, 10, N'هدية', {now})
            """));
        Assert.Equal(547, error.Number); // the check constraint

        // Down, then up again: the same.
        await migrator.MigrateAsync(Before);
        Assert.DoesNotContain(Hold, await db.Database.GetAppliedMigrationsAsync());
        await migrator.MigrateAsync();
        await AssertReleasedAtOnce();

        async Task AssertReleasedAtOnce()
        {
            var rows = await db.Database
                .SqlQuery<Row>($"SELECT Id, Type, CreatedAt, AvailableAt, ReversedTransactionId FROM PointTransactions WHERE UserId = {author.Id}")
                .ToListAsync();
            Assert.Equal(4, rows.Count);
            Assert.All(rows.Where(r => r.Type is "GiftReceived" or "PrivilegeRevenue"), r => Assert.Equal(r.CreatedAt, r.AvailableAt));
            Assert.All(rows.Where(r => r.Type is not ("GiftReceived" or "PrivilegeRevenue")), r => Assert.Null(r.AvailableAt));
            Assert.All(rows, r => Assert.Null(r.ReversedTransactionId));
            Assert.Equal(3700m, await db.Database
                .SqlQuery<decimal>($"SELECT CurrentBalance AS Value FROM UserWallets WHERE UserId = {author.Id}").SingleAsync());
        }
    }
}
