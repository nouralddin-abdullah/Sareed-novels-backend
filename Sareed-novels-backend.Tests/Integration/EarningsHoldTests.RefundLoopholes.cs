using System.Data.Common;
using Application.Gifts.Commands.SendGift;
using Application.Wallet;
using Application.Wallet.DTOs;
using Domain.Constants;
using Domain.Entities;
using Infrastructure.PlayBilling;
using Infrastructure.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;

namespace Sareed_novels_backend.Tests.Integration;

/// <summary>
/// The review of #22 (#27): the ways refunded Google Play points could still leave as cash (scenarios G, I and C: bought
/// points or held gifts withdrawable once an author had spent earnings; A and B: a clawback that only looked at payments
/// made after the refunded purchase; D: one more account in between), the deadlock between a refund and a gift, and the
/// retry of a gift or subscription SQL Server picks as a deadlock victim.
/// </summary>
public partial class EarningsHoldTests
{
    private static readonly TimeSpan Minute = TimeSpan.FromMinutes(1);

    /// <summary>A gift of exactly <paramref name="points"/> to the novel, through the gift handler, with its answer.</summary>
    private async Task<Application.Gifts.Commands.SendGift.OperationResult> GiftResult(User sender, Novel novel, decimal points)
    {
        var gift = new Gift { Id = Guid.NewGuid(), Name = "Rose", NameAr = "وردة", ImageUrl = "https://example.test/rose.png", Cost = points };
        await using (var db = database.CreateContext())
        {
            db.Gifts.Add(gift);
            await db.SaveChangesAsync();
        }

        await using var request = NewRequest();
        return await request.SendGift(sender).Handle(new SendGiftCommand { GiftId = gift.Id, NovelId = novel.Id, Count = 1 }, CancellationToken.None);
    }

    /// <summary>Two users, the first with the lower id (ordinal), which is the order a transfer locks their wallets in.</summary>
    private async Task<(User First, User Second)> SeedUsersInIdOrder()
    {
        var (a, b) = (await SeedUser(), await SeedUser());
        return string.CompareOrdinal(a.Id, b.Id) < 0 ? (a, b) : (b, a);
    }

    // ===== 1. Held gifts and bought points never become withdrawable once an author has spent earnings =====

    [Fact]
    public async Task G_a_gift_to_an_author_who_spent_their_released_earnings_is_held_like_any_other()
    {
        var author = await AuthorWithReleasedEarnings(1000);
        Assert.True(await Gift(author, await SeedNovel(await SeedUser()), 1000)); // gives all of it away: balance 0
        clock.Advance(Minute);

        // A fraudster buys 1000 on Google Play and gives them to the author.
        var fraud = await SeedUser();
        var purchase = await BuyOnPlay(fraud);
        Assert.True(await Gift(fraud, await SeedNovel(author), 1000));
        clock.Advance(Minute);

        var wallet = await Withdrawable(author);
        Assert.Equal((1000m, 0m, 1000m), (wallet.Balance, wallet.Withdrawable, wallet.PendingEarnings));
        var refused = await RequestWithdrawal(author, 1000);
        Assert.Equal((false, WithdrawalMessages.NotWithdrawableCode), (refused.Success, refused.Code));
        Assert.Empty(await Withdrawals(author));

        // The refund takes the gift back while it is still held: nobody is left below zero.
        await Refund(purchase);
        Assert.Equal((0m, 0m), (await Balance(fraud), await Balance(author)));
        await AssertLedgerAddsUp(author);
    }

    [Fact]
    public async Task I_gifting_a_top_up_back_and_forth_never_makes_more_than_it_withdrawable()
    {
        var (a, c, admin) = (await SeedUser(), await SeedUser(), await SeedUser());
        var (novelA, novelC) = (await SeedNovel(a), await SeedNovel(c));
        await TopUp(a, 1000); // the only real money
        for (var round = 0; round < 5; round++)
        {
            Assert.True(await Gift(a, novelC, 1000));
            clock.Advance(Minute);
            Assert.True(await Gift(c, novelA, 1000));
            clock.Advance(Minute);
        }
        clock.Advance(Hold);
        var purchase = await BuyOnPlay(a, "points_5000");

        // 5000 received in gifts, but 4000 of them were given back: 1000 is A's to withdraw, the 5000 bought never.
        var wallet = await Withdrawable(a);
        Assert.Equal((6000m, 1000m), (wallet.Balance, wallet.Withdrawable));
        Assert.False((await RequestWithdrawal(a, 5000)).Success);
        Assert.True((await RequestWithdrawal(a, 1000)).Success);
        Assert.True((await Approve(admin, Assert.Single(await Withdrawals(a)).Id)).Success);

        // The pack's refund comes out of the pack: A isn't left owing anything.
        await Refund(purchase);
        Assert.Equal(0m, await Balance(a));
        var other = await Withdrawable(c);
        Assert.Equal((0m, 0m, 0m), (other.Balance, other.Withdrawable, other.PendingEarnings));
        await AssertLedgerAddsUp(a);
    }

    [Fact]
    public async Task C_points_an_author_buys_after_giving_their_earnings_away_are_not_withdrawable()
    {
        var author = await AuthorWithReleasedEarnings(1000);
        Assert.True(await Gift(author, await SeedNovel(await SeedUser()), 1000));
        clock.Advance(Minute);

        await BuyOnPlay(author);

        var wallet = await Withdrawable(author);
        Assert.Equal((1000m, 0m, 0m), (wallet.Balance, wallet.Withdrawable, wallet.Payable));
        var refused = await RequestWithdrawal(author, 1000);
        Assert.Equal((false, WithdrawalMessages.NotWithdrawableCode), (refused.Success, refused.Code));
    }

    [Fact]
    public async Task Earnings_received_while_below_zero_pay_the_debt_first()
    {
        // A reader's refund left them 700 below zero: the author's hold on what they gave had ended.
        var (reader, author, fan) = (await SeedUser(), await SeedUser(), await SeedUser());
        var purchase = await BuyOnPlay(reader);
        Assert.True(await Gift(reader, await SeedNovel(author), 700));
        clock.Advance(Hold + Minute);
        await Refund(purchase);
        Assert.Equal(-700m, await Balance(reader));

        // The reader also writes: a fan gives them 1000, then they top up 5000.
        await TopUp(fan, 1000);
        Assert.True(await Gift(fan, await SeedNovel(reader), 1000));
        clock.Advance(Minute);
        await TopUp(reader, 5000);
        clock.Advance(Hold + Minute);

        // 700 of the gift paid the debt: 300 of it is left to withdraw.
        var wallet = await Withdrawable(reader);
        Assert.Equal((5300m, 300m), (wallet.Balance, wallet.Withdrawable));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)] // production's ledger has no rows from before #22
    public async Task A_withdrawal_approved_before_the_hold_existed_does_not_count_against_new_earnings(bool inLedger)
    {
        // Before #22 any balance could be withdrawn: 2000 of a 5000 top-up were paid out.
        var author = await SeedUser();
        var start = clock.UtcNow;
        await using (var db = database.CreateContext())
        {
            db.UserWallets.Add(new UserWallet { Id = Guid.NewGuid(), UserId = author.Id, CurrentBalance = 3000 });
            db.WithdrawalRequests.Add(new WithdrawalRequest
            {
                Id = Guid.NewGuid(), UserId = author.Id, PointsRequested = 2000, WithdrawalMethod = PaymentMethod.InstaPay,
                PaymentDetails = "01000000000", BaseAmountEGP = 200, TaxDeducted = 20, NetAmountEGP = 180,
                Status = RequestStatus.Approved, RequestedAt = start.AddDays(-91), ProcessedAt = start.AddDays(-90)
            });
            if (inLedger)
            {
                db.PointTransactions.AddRange(
                    LegacyRow(author, TransactionType.RechargeApproved, 5000, 0, start.AddDays(-100)),
                    LegacyRow(author, TransactionType.WithdrawalApproved, -2000, 5000, start.AddDays(-90)));
            }
            await db.SaveChangesAsync();
        }

        // Since then a reader gave the author 1000, and its hold has ended.
        var reader = await SeedUser();
        await TopUp(reader, 1000);
        Assert.True(await Gift(reader, await SeedNovel(author), 1000));
        clock.Advance(Hold + Minute);

        var wallet = await Withdrawable(author);
        Assert.Equal((4000m, 1000m), (wallet.Balance, wallet.Withdrawable));
        Assert.True((await RequestWithdrawal(author, 1000)).Success);
    }

    private static PointTransaction LegacyRow(User user, string type, decimal amount, decimal before, DateTime at) => new()
    {
        Id = Guid.NewGuid(), UserId = user.Id, Type = type, Amount = amount, BalanceBefore = before, BalanceAfter = before + amount,
        Description = "قيد قديم", CreatedAt = at
    };

    // ===== 2. A refund takes back every held earning the buyer paid for, not only those paid after the purchase =====

    [Fact]
    public async Task A_refunding_two_purchases_takes_back_both_gifts_they_paid_for()
    {
        var (buyer, alt) = (await SeedUser(), await SeedUser());
        var novel = await SeedNovel(alt);
        var first = await BuyOnPlay(buyer);
        Assert.True(await Gift(buyer, novel, 1000));
        clock.Advance(Minute);
        var second = await BuyOnPlay(buyer);
        Assert.True(await Gift(buyer, novel, 1000));
        clock.Advance(Minute);

        await Refund(first); // takes back the newest gift
        clock.Advance(Minute);
        await Refund(second); // and the other one, although it was paid before this purchase

        Assert.Equal((0m, 0m), (await Balance(buyer), await Balance(alt)));
        clock.Advance(Hold);
        var wallet = await Withdrawable(alt);
        Assert.Equal((0m, 0m, 0m), (wallet.Balance, wallet.Withdrawable, wallet.PendingEarnings));
        Assert.False((await RequestWithdrawal(alt, 1000)).Success);
        await AssertLedgerAddsUp(buyer);
        await AssertLedgerAddsUp(alt);
    }

    [Fact]
    public async Task B_a_refund_takes_back_a_gift_paid_before_the_refunded_purchase()
    {
        var (buyer, alt) = (await SeedUser(), await SeedUser());
        var first = await BuyOnPlay(buyer);
        Assert.True(await Gift(buyer, await SeedNovel(alt), 1000));
        clock.Advance(Minute);
        var second = await BuyOnPlay(buyer);
        clock.Advance(Minute);

        await Refund(first); // the second pack's points cover it: nothing to take back
        Assert.Equal((0m, 1000m), (await Balance(buyer), await Balance(alt)));
        await Refund(second); // 1000 short: the gift, paid before this purchase, is taken back

        Assert.Equal((0m, 0m), (await Balance(buyer), await Balance(alt)));
        clock.Advance(Hold);
        Assert.Equal(0m, (await Withdrawable(alt)).Withdrawable);
        Assert.False((await RequestWithdrawal(alt, 1000)).Success);
    }

    // ===== 3. The clawback follows the points on to whom they were given next =====

    [Fact]
    public async Task D_a_gift_passed_on_to_another_account_is_taken_back_from_that_account()
    {
        var (buyer, h, c) = (await SeedUser(), await SeedUser(), await SeedUser());
        var purchase = await BuyOnPlay(buyer);
        Assert.True(await Gift(buyer, await SeedNovel(h), 1000));
        clock.Advance(Minute);
        Assert.True(await Gift(h, await SeedNovel(c), 1000)); // H passes it on to C
        clock.Advance(Minute);

        await Refund(purchase);

        Assert.Equal((0m, 0m, 0m), (await Balance(buyer), await Balance(h), await Balance(c)));
        clock.Advance(Hold);
        var wallet = await Withdrawable(c);
        Assert.Equal((0m, 0m, 0m), (wallet.Balance, wallet.Withdrawable, wallet.PendingEarnings));
        Assert.False((await RequestWithdrawal(c, 1000)).Success);
    }

    [Fact]
    public async Task The_clawback_follows_the_points_three_accounts_past_the_buyers_own_gifts_and_no_further()
    {
        // The buyer's 1000 go on from account to account while they are held: buyer, h1, h2, h3, h4, then c.
        var buyer = await SeedUser();
        var chain = new List<User>();
        for (var i = 0; i < 5; i++)
        {
            chain.Add(await SeedUser());
        }
        var purchase = await BuyOnPlay(buyer);
        var from = buyer;
        foreach (var next in chain)
        {
            Assert.True(await Gift(from, await SeedNovel(next), 1000));
            clock.Advance(Minute);
            from = next;
        }

        await Refund(purchase);

        // Taken back from h1 (the buyer's own gift), then from h2, h3 and h4 as each account in turn went below zero; h4's
        // gift to c is one account too far: h4 is left 1000 below zero and c keeps it.
        Assert.Equal(0m, await Balance(buyer));
        var balances = new List<decimal>();
        foreach (var user in chain)
        {
            balances.Add(await Balance(user));
            await AssertLedgerAddsUp(user);
        }
        Assert.Equal([0m, 0m, 0m, -1000m, 1000m], balances);
    }

    [Fact]
    public async Task Points_given_back_to_the_buyer_are_taken_back_from_the_buyer_and_the_cascade_ends()
    {
        var (buyer, a, d) = (await SeedUser(), await SeedUser(), await SeedUser());
        var purchase = await BuyOnPlay(buyer);
        Assert.True(await Gift(buyer, await SeedNovel(a), 1000));
        clock.Advance(Minute);
        Assert.True(await Gift(a, await SeedNovel(buyer), 1000)); // A gives it back
        clock.Advance(Minute);
        // The buyer spends it where nothing can be taken back (D's earnings were released at once then).
        await using (var request = NewRequest())
        {
            await WalletTesting.Wallet(request.Db, clock, holdDays: 0)
                .TransferPointsAsync(buyer.Id, d.Id, 1000, TransactionType.GiftSent, TransactionType.GiftReceived, "هدية", "هدية");
        }
        clock.Advance(Minute);

        await Refund(purchase);

        // A's gift is taken back, which leaves A below zero; A's gift back to the buyer is then taken back from the buyer,
        // whose payments were walked already: the loss stays with the buyer, and D keeps what was released.
        Assert.Equal((-1000m, 0m, 1000m), (await Balance(buyer), await Balance(a), await Balance(d)));
        await AssertLedgerAddsUp(buyer);
        await AssertLedgerAddsUp(a);
    }

    [Fact]
    public async Task A_refund_does_not_take_back_earnings_of_an_account_deleted_since()
    {
        var (buyer, author) = (await SeedUser(), await SeedUser());
        var purchase = await BuyOnPlay(buyer);
        Assert.True(await Gift(buyer, await SeedNovel(author), 1000));
        clock.Advance(Minute);

        // The author deletes their account: their balance, held earnings included, is forfeited already.
        await using (var db = database.CreateContext())
        {
            var cache = new TokenCutoffCache(new MemoryCache(new MemoryCacheOptions { SizeLimit = 100 }));
            var deletion = new AccountDeletionService(db, new UpperInvariantLookupNormalizer(),
                new TokenRevocationService(db, cache, TimeProvider.System), cache, new InMemoryObjectStorage("https://files.test"),
                TimeProvider.System, NullLogger<AccountDeletionService>.Instance);
            Assert.True((await deletion.DeleteAsync(author.Id)).Deleted);
        }
        Assert.Equal(0m, await Balance(author));

        await Refund(purchase);

        Assert.Equal((-1000m, 0m), (await Balance(buyer), await Balance(author)));
        Assert.DoesNotContain(await Ledger(author), t => t.Type == TransactionType.EarningReversed);
        Assert.DoesNotContain(await Ledger(buyer), t => t.Type == TransactionType.EarningReversed);
    }

    // ===== 4. Deadlocks =====

    /// <summary>
    /// Holds the command that locks <paramref name="userId"/>'s wallet (UPDLOCK on UserWallets), the first time it runs,
    /// right after it took the lock, until the test releases it.
    /// </summary>
    private sealed class PauseAtWalletLock(string userId) : DbCommandInterceptor
    {
        private int fired;

        public TaskCompletionSource Reached { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public override async ValueTask<DbDataReader> ReaderExecutedAsync(DbCommand command, CommandExecutedEventData eventData,
            DbDataReader result, CancellationToken cancellationToken = default)
        {
            if (command.CommandText.Contains("UPDLOCK", StringComparison.Ordinal)
                && command.CommandText.Contains("UserWallets", StringComparison.Ordinal)
                && command.Parameters.Cast<DbParameter>().Any(p => userId.Equals(p.Value))
                && Interlocked.Exchange(ref fired, 1) == 0)
            {
                Reached.TrySetResult();
                await Release.Task.WaitAsync(TimeSpan.FromSeconds(60), cancellationToken);
            }
            return result;
        }
    }

    /// <summary>Waits until some request in the test's database waits for a lock another one holds.</summary>
    private async Task WaitUntilALockIsAwaited()
    {
        await using var db = database.CreateContext();
        var until = DateTime.UtcNow.AddSeconds(30);
        while (DateTime.UtcNow < until)
        {
            var waiting = await db.Database
                .SqlQuery<int>($"SELECT COUNT(*) AS Value FROM sys.dm_exec_requests WHERE blocking_session_id <> 0 AND database_id = DB_ID()")
                .SingleAsync();
            if (waiting > 0)
            {
                return;
            }
            await Task.Delay(20);
        }
        Assert.Fail("Nothing waited for a lock within 30 seconds");
    }

    [Fact]
    public async Task A_gift_from_an_author_back_to_the_buyer_during_the_refund_does_not_deadlock()
    {
        // A gift locks its two wallets in user-id order; the refund used to lock the buyer's, then the authors'. With the
        // author's id first, a gift from the author back to the buyer, sent while the refund held the buyer's wallet,
        // deadlocked with it (9 runs in 10 when it happened by chance; every time here).
        var (author, buyer) = await SeedUsersInIdOrder();
        await TopUp(author, 500);
        var purchase = await BuyOnPlay(buyer);
        Assert.True(await Gift(buyer, await SeedNovel(author), 1000)); // held: the refund takes it back
        var buyersNovel = await SeedNovel(buyer);
        clock.Advance(Minute);

        var pause = new PauseAtWalletLock(buyer.Id);
        var refund = Task.Run(async () =>
        {
            await using var request = NewRequest(pause);
            return await request.Play.ApplyVoidAsync(new PlayVoidedPurchase(purchase.Token, purchase.OrderId, clock.UtcNow, 1, 0));
        });
        await pause.Reached.Task.WaitAsync(TimeSpan.FromSeconds(30)); // the refund holds the buyer's wallet

        var gift = Task.Run(() => GiftResult(author, buyersNovel, 100));
        await WaitUntilALockIsAwaited(); // the gift waits for a wallet the refund holds
        pause.Release.TrySetResult();

        Assert.True(await refund.WaitAsync(TimeSpan.FromSeconds(60)));
        var sent = await gift.WaitAsync(TimeSpan.FromSeconds(60));
        Assert.True(sent.Success, $"{sent.Code}: {sent.Message}");
        // The refund took the held gift back from the author (1500 - 1000), then the author gave 100 to the buyer.
        Assert.Equal((100m, 400m), (await Balance(buyer), await Balance(author)));
        await AssertLedgerAddsUp(buyer);
        await AssertLedgerAddsUp(author);
    }

    /// <summary>A transaction of the test's own, which SQL Server keeps when it deadlocks with the API (a higher priority).</summary>
    private sealed class Rival : IAsyncDisposable
    {
        private readonly SqlConnection connection;
        private readonly SqlTransaction transaction;

        private Rival(SqlConnection connection, SqlTransaction transaction) => (this.connection, this.transaction) = (connection, transaction);

        public static async Task<Rival> BeginAsync(string connectionString)
        {
            var connection = new SqlConnection(connectionString);
            await connection.OpenAsync();
            await using (var priority = new SqlCommand("SET DEADLOCK_PRIORITY HIGH", connection))
            {
                await priority.ExecuteNonQueryAsync();
            }
            return new Rival(connection, (SqlTransaction)await connection.BeginTransactionAsync());
        }

        /// <summary>Locks the user's wallet row until the rival's transaction ends.</summary>
        public async Task LockWalletAsync(string userId)
        {
            await using var command = new SqlCommand("SELECT CurrentBalance FROM UserWallets WITH (UPDLOCK, ROWLOCK) WHERE UserId = @userId",
                connection, transaction);
            command.Parameters.AddWithValue("@userId", userId);
            await command.ExecuteScalarAsync();
        }

        public Task CommitAsync() => transaction.CommitAsync();

        public async ValueTask DisposeAsync()
        {
            await transaction.DisposeAsync();
            await connection.DisposeAsync();
        }
    }

    [Fact]
    public async Task A_gift_sql_server_picks_as_a_deadlock_victim_is_sent_again_and_paid_once()
    {
        var (reader, author) = await SeedUsersInIdOrder(); // the gift locks the reader's wallet first
        await TopUp(reader, 1000);
        await TopUp(author, 10);
        var novel = await SeedNovel(author);

        // Another transaction locks the two wallets the other way round, and wins the deadlock.
        await using var rival = await Rival.BeginAsync(database.ConnectionString);
        await rival.LockWalletAsync(author.Id);
        var gift = Task.Run(() => GiftResult(reader, novel, 300)); // locks the reader's wallet, then waits for the author's
        await WaitUntilALockIsAwaited();
        await rival.LockWalletAsync(reader.Id); // the deadlock: SQL Server rolls the gift's transaction back
        await rival.CommitAsync();

        var sent = await gift.WaitAsync(TimeSpan.FromSeconds(60));
        Assert.True(sent.Success, $"{sent.Code}: {sent.Message}");
        Assert.Equal((700m, 310m), (await Balance(reader), await Balance(author)));
        Assert.Single(await Ledger(reader), t => t.Type == TransactionType.GiftSent);
        Assert.Single(await Ledger(author), t => t.Type == TransactionType.GiftReceived);
        await using var db = database.CreateContext();
        Assert.Equal(1, await db.GiftTransactions.CountAsync(g => g.SenderId == reader.Id));
    }

    [Fact]
    public async Task A_subscription_sql_server_picks_as_a_deadlock_victim_is_made_again_and_paid_once()
    {
        var (reader, author) = await SeedUsersInIdOrder();
        await TopUp(reader, 1000);
        await TopUp(author, 10);
        var novel = await SeedNovel(author, privilegeCost: 300);

        await using var rival = await Rival.BeginAsync(database.ConnectionString);
        await rival.LockWalletAsync(author.Id);
        var subscribe = Task.Run(async () =>
        {
            await using var request = NewRequest();
            return await request.Privileges.SubscribeToPrivilegeAsync(novel.Id, reader.Id);
        });
        await WaitUntilALockIsAwaited();
        await rival.LockWalletAsync(reader.Id);
        await rival.CommitAsync();

        var subscribed = await subscribe.WaitAsync(TimeSpan.FromSeconds(60));
        Assert.True(subscribed.Success, $"{subscribed.Code}: {subscribed.Message}");
        Assert.Equal((700m, 310m), (await Balance(reader), await Balance(author)));
        Assert.Single(await Ledger(reader), t => t.Type == TransactionType.PrivilegeSubscription);
        await using var db = database.CreateContext();
        Assert.Equal(1, await db.NovelPrivilegeSubscriptions.CountAsync(s => s.UserId == reader.Id));
    }
}
