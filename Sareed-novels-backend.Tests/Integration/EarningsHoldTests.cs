using Application.Gifts.Commands.SendGift;
using Application.Services;
using Application.Users;
using Application.Wallet;
using Application.Wallet.Commands.ApproveWithdrawal;
using Application.Wallet.Commands.RequestWithdrawal;
using Application.Wallet.DTOs;
using Domain.Constants;
using Domain.Entities;
using Infrastructure.Persistence;
using Infrastructure.PlayBilling;
using Infrastructure.Repositories;
using Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Sareed_novels_backend.Tests.Integration;

/// <summary>
/// Payouts (#22) against SQL Server, through the real handlers and services, with a clock the tests move: only earnings
/// can be withdrawn, 30 days after they were received; bought points never; pending requests reserve their points and
/// approval checks again; a Google Play refund takes back the earnings still on hold that the refunded points paid for,
/// newest first, up to what the buyer's balance can't cover, and gives that back to the buyer. The arithmetic alone:
/// Unit/EarningsHoldRulesTests.
/// </summary>
public partial class EarningsHoldTests(SqlServerDatabase database) : IClassFixture<SqlServerDatabase>
{
    private static readonly TimeSpan Hold = TimeSpan.FromDays(30);

    private readonly MutableClock clock = new(DateTime.UtcNow);
    private readonly FakeGooglePlay google = new();

    /// <summary>
    /// One API request: its own DbContext and scoped services, all on the test's clock; its commands pass through
    /// <paramref name="interceptors"/>, if any.
    /// </summary>
    private sealed class Request(SqlServerDatabase database, MutableClock clock, FakeGooglePlay google, params IInterceptor[] interceptors)
        : IAsyncDisposable
    {
        public ApplicationDbContext Db { get; } = database.CreateContext(interceptors);
        public TransactionManager Transactions => new(Db);
        public WalletService Wallet => WalletTesting.Wallet(Db, clock);

        public PlayBillingService Play => new(google.Connection(), google.Api(), new PlayPurchaseRepository(Db),
            new UserWalletRepository(Db), new PointTransactionRepository(Db), Wallet, Transactions, clock,
            NullLogger<PlayBillingService>.Instance);

        public SendGiftCommandHandler SendGift(User sender) => new(NullLogger<SendGiftCommandHandler>.Instance,
            new GiftRepository(Db), new GiftTransactionRepository(Db), new NovelsRepository(Db), SignedIn(sender), Wallet,
            Transactions, Substitute.For<IServiceScopeFactory>());

        public PrivilegeService Privileges => new(NullLogger<PrivilegeService>.Instance, new NovelPrivilegeRepository(Db),
            new PrivilegeSubscriptionRepository(Db), new NovelsRepository(Db), new ChaptersRepository(Db), Wallet, Transactions,
            Substitute.For<IServiceScopeFactory>());

        public RequestWithdrawalCommandHandler RequestWithdrawal(User user) => new(NullLogger<RequestWithdrawalCommandHandler>.Instance,
            SignedIn(user), new WithdrawalRequestRepository(Db), new PointCalculationService(), Wallet, Transactions);

        public ApproveWithdrawalCommandHandler ApproveWithdrawal(User admin) => new(NullLogger<ApproveWithdrawalCommandHandler>.Instance,
            SignedIn(admin), new WithdrawalRequestRepository(Db), Wallet, Transactions);

        public ValueTask DisposeAsync() => Db.DisposeAsync();
    }

    private Request NewRequest(params IInterceptor[] interceptors) => new(database, clock, google, interceptors);

    private static IUserContext SignedIn(User user)
    {
        var context = Substitute.For<IUserContext>();
        context.GetCurrentUser().Returns(new CurrentUser(user.Id, user.Email!, user.UserName!, user.DisplayName));
        return context;
    }

    private sealed record Outcome(bool Success, string? Code, string Message);

    // ===== Seeding and acting =====

    private async Task<User> SeedUser()
    {
        var user = Seed.User();
        await using var db = database.CreateContext();
        db.Users.Add(user);
        await db.SaveChangesAsync();
        return user;
    }

    private async Task<Novel> SeedNovel(User author, decimal? privilegeCost = null)
    {
        var novel = Seed.Novel(author, "رواية " + Seed.Marker());
        await using var db = database.CreateContext();
        db.Novels.Add(novel);
        if (privilegeCost is { } cost)
        {
            db.NovelPrivileges.Add(new NovelPrivilege
            {
                Id = Guid.NewGuid(), NovelId = novel.Id, IsEnabled = true, SubscriptionCost = cost, CurrentLockedCount = 5,
                PrivilegeStartSequence = 11
            });
        }
        await db.SaveChangesAsync();
        return novel;
    }

    /// <summary>A website top-up an admin approved: bought points.</summary>
    private async Task TopUp(User user, decimal points)
    {
        await using var request = NewRequest();
        await request.Wallet.AddPointsAsync(user.Id, points, TransactionType.RechargeApproved, "شحن");
    }

    /// <summary>A point pack bought in the app, verified and credited: bought points.</summary>
    private async Task<FakePlayPurchase> BuyOnPlay(User user, string productId = "points_1000")
    {
        var purchase = google.Buy(user.Id, productId);
        await using var request = NewRequest();
        var result = await request.Play.VerifyPurchaseAsync(user.Id, productId, purchase.Token, purchase.OrderId);
        Assert.True(result.Success, result.Error?.ToString());
        return purchase;
    }

    /// <summary>Google voided the purchase (a refund) and the server applied it.</summary>
    private async Task Refund(FakePlayPurchase purchase) => Assert.True(await ApplyVoid(purchase));

    /// <summary>The server applies Google's void; false when it had been applied already.</summary>
    private async Task<bool> ApplyVoid(FakePlayPurchase purchase)
    {
        await using var request = NewRequest();
        return await request.Play.ApplyVoidAsync(new PlayVoidedPurchase(purchase.Token, purchase.OrderId, clock.UtcNow, 1, 0));
    }

    private async Task<Guid> PlayPurchaseId(FakePlayPurchase purchase)
    {
        await using var db = database.CreateContext();
        return await db.PlayPurchases.Where(p => p.PurchaseToken == purchase.Token).Select(p => p.Id).SingleAsync();
    }

    /// <summary>A gift of exactly <paramref name="points"/> to the novel, through the gift handler.</summary>
    private async Task<bool> Gift(User sender, Novel novel, decimal points)
    {
        var gift = new Gift { Id = Guid.NewGuid(), Name = "Rose", NameAr = "وردة", ImageUrl = "https://example.test/rose.png", Cost = points };
        await using (var db = database.CreateContext())
        {
            db.Gifts.Add(gift);
            await db.SaveChangesAsync();
        }

        await using var request = NewRequest();
        var result = await request.SendGift(sender).Handle(new SendGiftCommand { GiftId = gift.Id, NovelId = novel.Id, Count = 1 }, CancellationToken.None);
        return result.Success;
    }

    /// <summary>An author with <paramref name="points"/> received as a gift whose hold has ended (the clock moves past it).</summary>
    private async Task<User> AuthorWithReleasedEarnings(decimal points)
    {
        var (reader, author) = (await SeedUser(), await SeedUser());
        await TopUp(reader, points);
        Assert.True(await Gift(reader, await SeedNovel(author), points));
        clock.Advance(Hold + TimeSpan.FromMinutes(1));
        return author;
    }

    private async Task<WithdrawableBalance> Withdrawable(User user)
    {
        await using var request = NewRequest();
        return await request.Wallet.GetWithdrawableAsync(user.Id);
    }

    private async Task<Outcome> RequestWithdrawal(User user, int points)
    {
        await using var request = NewRequest();
        var result = await request.RequestWithdrawal(user).Handle(new RequestWithdrawalCommand
        {
            PointsRequested = points, WithdrawalMethod = PaymentMethod.InstaPay, PaymentDetails = "01000000000"
        }, CancellationToken.None);
        return new Outcome(result.Success, result.Code, result.Message);
    }

    private async Task<Outcome> Approve(User admin, Guid withdrawalId)
    {
        await using var request = NewRequest();
        var result = await request.ApproveWithdrawal(admin).Handle(new ApproveWithdrawalCommand { RequestId = withdrawalId }, CancellationToken.None);
        return new Outcome(result.Success, result.Code, result.Message);
    }

    private async Task<List<WithdrawalRequest>> Withdrawals(User user)
    {
        await using var db = database.CreateContext();
        return await db.WithdrawalRequests.AsNoTracking().Where(w => w.UserId == user.Id).ToListAsync();
    }

    private async Task<decimal> Balance(User user)
    {
        await using var db = database.CreateContext();
        return await db.UserWallets.Where(w => w.UserId == user.Id).Select(w => w.CurrentBalance).SingleOrDefaultAsync();
    }

    private async Task<List<PointTransaction>> Ledger(User user)
    {
        await using var db = database.CreateContext();
        return await db.PointTransactions.AsNoTracking().Where(t => t.UserId == user.Id).OrderBy(t => t.CreatedAt).ToListAsync();
    }

    /// <summary>Every row follows from the one before, and together they make the balance.</summary>
    private async Task AssertLedgerAddsUp(User user)
    {
        var ledger = await Ledger(user);
        Assert.All(ledger, t => Assert.Equal(t.BalanceBefore + t.Amount, t.BalanceAfter));
        Assert.Equal(await Balance(user), ledger.Sum(t => t.Amount));
    }

    // ===== Rules 1 and 2: earnings only, after the hold =====

    [Fact]
    public async Task A_gift_can_be_withdrawn_30_days_after_it_was_received_and_not_before()
    {
        var (reader, author) = (await SeedUser(), await SeedUser());
        await TopUp(reader, 3000);
        var receivedAt = clock.UtcNow;
        Assert.True(await Gift(reader, await SeedNovel(author), 1500));

        var held = await Withdrawable(author);
        Assert.Equal((1500m, 0m, 1500m), (held.Balance, held.Withdrawable, held.PendingEarnings));
        Assert.Equal(receivedAt + Hold, held.NextReleaseAt!.Value, TimeSpan.FromSeconds(1));
        Assert.Equal(DateTimeKind.Utc, held.NextReleaseAt.Value.Kind);

        var refused = await RequestWithdrawal(author, 1000);
        Assert.Equal((false, WithdrawalMessages.NotWithdrawableCode), (refused.Success, refused.Code));
        Assert.StartsWith("لا توجد نقاط قابلة للسحب الآن، وتصبح أرباحك التالية قابلة للسحب خلال 30 يومًا.", refused.Message);

        clock.Advance(Hold - TimeSpan.FromMinutes(1));
        var almost = await Withdrawable(author);
        Assert.Equal((0m, 1500m), (almost.Withdrawable, almost.PendingEarnings));
        Assert.StartsWith("لا توجد نقاط قابلة للسحب الآن، وتصبح أرباحك التالية قابلة للسحب خلال يوم واحد.",
            (await RequestWithdrawal(author, 1000)).Message);

        clock.Advance(TimeSpan.FromMinutes(2));
        var released = await Withdrawable(author);
        Assert.Equal((1500m, 0m, (DateTime?)null), (released.Withdrawable, released.PendingEarnings, released.NextReleaseAt));
        Assert.True((await RequestWithdrawal(author, 1000)).Success);
        Assert.Equal(500m, (await Withdrawable(author)).Withdrawable);

        // The reader's points were bought: never withdrawable.
        Assert.Equal((1500m, 0m), ((await Withdrawable(reader)).Balance, (await Withdrawable(reader)).Withdrawable));

        // The earning row says when it is released, and shares its id with the reader's payment (the gift record).
        var earning = Assert.Single(await Ledger(author), t => t.Type == TransactionType.GiftReceived);
        Assert.Equal(earning.CreatedAt + Hold, earning.AvailableAt);
        var payment = Assert.Single(await Ledger(reader), t => t.Type == TransactionType.GiftSent);
        Assert.Null(payment.AvailableAt);
        Assert.NotNull(earning.RelatedRequestId);
        Assert.Equal(earning.RelatedRequestId, payment.RelatedRequestId);
        await using var db = database.CreateContext();
        Assert.True(await db.GiftTransactions.AnyAsync(g => g.Id == earning.RelatedRequestId && g.SenderId == reader.Id));
    }

    [Fact]
    public async Task A_privilege_subscription_can_be_withdrawn_30_days_after_it_was_received_and_not_before()
    {
        var (reader, author) = (await SeedUser(), await SeedUser());
        var novel = await SeedNovel(author, privilegeCost: 1200);
        await TopUp(reader, 2000);
        await using (var request = NewRequest())
        {
            Assert.True((await request.Privileges.SubscribeToPrivilegeAsync(novel.Id, reader.Id)).Success);
        }

        var held = await Withdrawable(author);
        Assert.Equal((0m, 1200m), (held.Withdrawable, held.PendingEarnings));
        Assert.False((await RequestWithdrawal(author, 1000)).Success);

        clock.Advance(Hold + TimeSpan.FromMinutes(1));
        Assert.Equal(1200m, (await Withdrawable(author)).Withdrawable);
        Assert.True((await RequestWithdrawal(author, 1200)).Success);

        var revenue = Assert.Single(await Ledger(author), t => t.Type == TransactionType.PrivilegeRevenue);
        Assert.Equal(revenue.CreatedAt + Hold, revenue.AvailableAt);
        var paid = Assert.Single(await Ledger(reader), t => t.Type == TransactionType.PrivilegeSubscription);
        Assert.Equal(revenue.RelatedRequestId, paid.RelatedRequestId);
        await using var db = database.CreateContext();
        Assert.True(await db.NovelPrivilegeSubscriptions.AnyAsync(s => s.Id == revenue.RelatedRequestId && s.UserId == reader.Id));
    }

    [Fact]
    public async Task Topped_up_and_play_points_are_never_withdrawable_however_large_the_balance()
    {
        var (user, admin) = (await SeedUser(), await SeedUser());
        await TopUp(user, 50_000);
        await BuyOnPlay(user, "points_5000");
        clock.Advance(TimeSpan.FromDays(90));

        var wallet = await Withdrawable(user);
        Assert.Equal((55_000m, 0m, 0m), (wallet.Balance, wallet.Withdrawable, wallet.PendingEarnings));

        var refused = await RequestWithdrawal(user, 1000);
        Assert.Equal((false, WithdrawalMessages.NotWithdrawableCode), (refused.Success, refused.Code));
        Assert.Equal(
            "لا توجد نقاط قابلة للسحب الآن. تُسحب أرباح الهدايا واشتراكات الوصول المبكر وحدها، بعد 30 يومًا من استلامها، أما النقاط المشحونة أو المشتراة فلا تُسحب.",
            refused.Message);
        Assert.Empty(await Withdrawals(user));

        // A request made before #22 isn't paid either.
        var old = new WithdrawalRequest
        {
            Id = Guid.NewGuid(), UserId = user.Id, PointsRequested = 1000, WithdrawalMethod = PaymentMethod.InstaPay,
            PaymentDetails = "01000000000", BaseAmountEGP = 100, TaxDeducted = 10, NetAmountEGP = 90
        };
        await using (var db = database.CreateContext())
        {
            db.WithdrawalRequests.Add(old);
            await db.SaveChangesAsync();
        }
        var approval = await Approve(admin, old.Id);
        Assert.Equal((false, WithdrawalMessages.NotWithdrawableCode), (approval.Success, approval.Code));
        Assert.Equal("رصيد المستخدم القابل للسحب لا يكفي لهذا الطلب: القابل للسحب الآن 0 نقطة.", approval.Message);
        Assert.Equal(RequestStatus.Pending, Assert.Single(await Withdrawals(user)).Status);
        Assert.Equal(55_000m, await Balance(user));
    }

    [Fact]
    public async Task An_author_who_spends_points_spends_bought_points_first()
    {
        var author = await AuthorWithReleasedEarnings(1500);
        var other = await SeedNovel(await SeedUser());
        await TopUp(author, 2000);
        Assert.Equal((3500m, 1500m), ((await Withdrawable(author)).Balance, (await Withdrawable(author)).Withdrawable));

        Assert.True(await Gift(author, other, 1800)); // all of it from the 2000 bought
        Assert.Equal((1700m, 1500m), ((await Withdrawable(author)).Balance, (await Withdrawable(author)).Withdrawable));

        Assert.True(await Gift(author, other, 500)); // 200 bought, then 300 earned
        Assert.Equal((1200m, 1200m), ((await Withdrawable(author)).Balance, (await Withdrawable(author)).Withdrawable));
    }

    // ===== Rule 3: pending requests reserve, approval checks again =====

    [Fact]
    public async Task Pending_requests_reserve_their_points()
    {
        var author = await AuthorWithReleasedEarnings(2500);

        Assert.True((await RequestWithdrawal(author, 1500)).Success);
        Assert.Equal(1000m, (await Withdrawable(author)).Withdrawable);

        var refused = await RequestWithdrawal(author, 1500);
        Assert.Equal((false, WithdrawalMessages.NotWithdrawableCode), (refused.Success, refused.Code));
        Assert.StartsWith("يمكنك سحب 1000 نقطة فقط الآن. ", refused.Message);

        Assert.True((await RequestWithdrawal(author, 1000)).Success);
        Assert.Equal(0m, (await Withdrawable(author)).Withdrawable);
        Assert.Equal(2, (await Withdrawals(author)).Count);
    }

    [Fact]
    public async Task Requests_at_the_same_moment_cannot_both_use_the_same_earnings()
    {
        var author = await AuthorWithReleasedEarnings(2000);

        var results = await Task.WhenAll(Enumerable.Range(0, 6).Select(_ => Task.Run(() => RequestWithdrawal(author, 1500))));

        Assert.Single(results, r => r.Success);
        Assert.All(results.Where(r => !r.Success), r => Assert.Equal(WithdrawalMessages.NotWithdrawableCode, r.Code));
        Assert.Equal(1500, Assert.Single(await Withdrawals(author)).PointsRequested);
    }

    [Fact]
    public async Task Approval_checks_again_and_refuses_what_the_author_spent_since()
    {
        var admin = await SeedUser();
        var author = await AuthorWithReleasedEarnings(2000);
        Assert.True((await RequestWithdrawal(author, 1500)).Success);
        Assert.True(await Gift(author, await SeedNovel(await SeedUser()), 1000));

        var withdrawal = Assert.Single(await Withdrawals(author));
        var approval = await Approve(admin, withdrawal.Id);

        Assert.Equal((false, WithdrawalMessages.NotWithdrawableCode), (approval.Success, approval.Code));
        Assert.Equal("رصيد المستخدم القابل للسحب لا يكفي لهذا الطلب: القابل للسحب الآن 1000 نقطة.", approval.Message);
        Assert.Equal(RequestStatus.Pending, Assert.Single(await Withdrawals(author)).Status);
        Assert.Equal(1000m, await Balance(author));
        Assert.DoesNotContain(await Ledger(author), t => t.Type == TransactionType.WithdrawalApproved);
    }

    [Fact]
    public async Task Approval_refuses_what_a_refund_took_between_the_request_and_the_approval()
    {
        var admin = await SeedUser();
        var author = await AuthorWithReleasedEarnings(1000);
        Assert.True((await RequestWithdrawal(author, 1000)).Success);

        // Then the author buys points, gives them away and has the purchase refunded: the earnings paid for the gift.
        var purchase = await BuyOnPlay(author);
        Assert.True(await Gift(author, await SeedNovel(await SeedUser()), 1000));
        await Refund(purchase);
        Assert.Equal(0m, await Balance(author));

        var approval = await Approve(admin, Assert.Single(await Withdrawals(author)).Id);

        Assert.False(approval.Success);
        Assert.Equal(WithdrawalMessages.NotWithdrawableCode, approval.Code);
        Assert.Equal(RequestStatus.Pending, Assert.Single(await Withdrawals(author)).Status);
        Assert.Equal(0m, await Balance(author));
    }

    [Fact]
    public async Task An_approved_withdrawal_is_paid_and_counts_as_withdrawn()
    {
        var admin = await SeedUser();
        var author = await AuthorWithReleasedEarnings(2500);
        Assert.True((await RequestWithdrawal(author, 1000)).Success);

        Assert.True((await Approve(admin, Assert.Single(await Withdrawals(author)).Id)).Success);

        // Paid out of the released earnings: 1500 of them are left, and nothing is reserved any more.
        var wallet = await Withdrawable(author);
        Assert.Equal((1500m, 1500m, 0m, 1500m), (wallet.Balance, wallet.Released, wallet.PendingWithdrawals, wallet.Withdrawable));
        await AssertLedgerAddsUp(author);
    }

    // ===== Rule 4: a refund takes back held earnings =====

    [Fact]
    public async Task A_refund_takes_back_held_earnings_newest_first_up_to_the_deficit_and_gives_it_back_to_the_buyer()
    {
        var buyer = await SeedUser();
        var (a, b, c, d) = (await SeedUser(), await SeedUser(), await SeedUser(), await SeedUser());
        var (novelA, novelB, novelC, novelD) = (await SeedNovel(a), await SeedNovel(b), await SeedNovel(c), await SeedNovel(d));

        await TopUp(buyer, 300);
        Assert.True(await Gift(buyer, novelD, 100)); // before the purchase: not paid with it
        clock.Advance(TimeSpan.FromMinutes(1));
        var purchase = await BuyOnPlay(buyer); // 1200
        clock.Advance(TimeSpan.FromMinutes(1));
        Assert.True(await Gift(buyer, novelA, 400));
        clock.Advance(TimeSpan.FromMinutes(1));
        Assert.True(await Gift(buyer, novelB, 500));
        clock.Advance(TimeSpan.FromMinutes(1));
        Assert.True(await Gift(buyer, novelC, 200)); // 100 left
        clock.Advance(TimeSpan.FromDays(3));

        await Refund(purchase); // 100 - 1000 = -900: C's 200, B's 500, then 200 of A's 400

        Assert.Equal(0m, await Balance(buyer));
        Assert.Equal((200m, 0m, 0m, 100m), (await Balance(a), await Balance(b), await Balance(c), await Balance(d)));

        var purchaseId = await PlayPurchaseId(purchase);
        foreach (var (author, novel, taken) in new[] { (a, novelA, 200m), (b, novelB, 500m), (c, novelC, 200m) })
        {
            var ledger = await Ledger(author);
            var earning = Assert.Single(ledger, t => t.Type == TransactionType.GiftReceived);
            var reversal = Assert.Single(ledger, t => t.Type == TransactionType.EarningReversed);
            Assert.Equal(-taken, reversal.Amount);
            Assert.Equal(purchaseId, reversal.RelatedRequestId);
            Assert.Equal(earning.Id, reversal.ReversedTransactionId);
            Assert.Equal(novel.Id, reversal.NovelId);
            Assert.Null(reversal.AvailableAt);
            Assert.Equal($"أُلغيت أرباح {taken:0} نقطة لأن عملية الشراء التي جاءت منها استُرد مبلغها", reversal.Description);
            await AssertLedgerAddsUp(author);
        }
        Assert.DoesNotContain(await Ledger(d), t => t.Type == TransactionType.EarningReversed);

        var buyerLedger = await Ledger(buyer);
        Assert.Equal(-1000m, Assert.Single(buyerLedger, t => t.Type == TransactionType.PlayRefund).Amount);
        var returned = buyerLedger.Where(t => t.Type == TransactionType.EarningReversed).OrderBy(t => t.Amount).ToList();
        Assert.Equal([200m, 200m, 500m], returned.Select(t => t.Amount));
        Assert.All(returned, t => Assert.Equal(purchaseId, t.RelatedRequestId));
        Assert.Contains(returned, t => t.Description == "استُرجعت 500 نقطة من أرباح الكاتب وأُعيدت إلى رصيدك، لأن عملية الشراء التي دفعت منها استُرد مبلغها");
        await AssertLedgerAddsUp(buyer);

        // A keeps what wasn't needed: on hold until its release, then withdrawable.
        var held = await Withdrawable(a);
        Assert.Equal((0m, 200m), (held.Withdrawable, held.PendingEarnings));
        clock.Advance(Hold);
        Assert.Equal(200m, (await Withdrawable(a)).Withdrawable);
        Assert.Equal(0m, (await Withdrawable(c)).PendingEarnings + (await Withdrawable(c)).Withdrawable);
    }

    [Fact]
    public async Task Earnings_whose_hold_ended_are_not_taken_back()
    {
        var buyer = await SeedUser();
        var (a, b) = (await SeedUser(), await SeedUser());
        var purchase = await BuyOnPlay(buyer);
        Assert.True(await Gift(buyer, await SeedNovel(a), 600));
        clock.Advance(Hold + TimeSpan.FromMinutes(1)); // A's gift is released
        Assert.True(await Gift(buyer, await SeedNovel(b), 300));

        await Refund(purchase); // 100 - 1000 = -900: only B's 300 is still on hold

        Assert.Equal((-600m, 600m, 0m), (await Balance(buyer), await Balance(a), await Balance(b)));
        Assert.DoesNotContain(await Ledger(a), t => t.Type == TransactionType.EarningReversed);
        Assert.Equal(600m, (await Withdrawable(a)).Withdrawable);
        await AssertLedgerAddsUp(buyer);
    }

    [Fact]
    public async Task A_refund_the_buyers_balance_covers_takes_nothing_back()
    {
        var (buyer, author) = (await SeedUser(), await SeedUser());
        await TopUp(buyer, 2000);
        var purchase = await BuyOnPlay(buyer);
        Assert.True(await Gift(buyer, await SeedNovel(author), 800));

        await Refund(purchase); // 2200 - 1000 = 1200

        Assert.Equal((1200m, 800m), (await Balance(buyer), await Balance(author)));
        Assert.DoesNotContain(await Ledger(author), t => t.Type == TransactionType.EarningReversed);
        Assert.DoesNotContain(await Ledger(buyer), t => t.Type == TransactionType.EarningReversed);
        Assert.Equal(800m, (await Withdrawable(author)).PendingEarnings);
    }

    [Fact]
    public async Task A_refunded_privilege_subscription_is_taken_back_from_the_author()
    {
        var (buyer, author) = (await SeedUser(), await SeedUser());
        var novel = await SeedNovel(author, privilegeCost: 700);
        var purchase = await BuyOnPlay(buyer);
        await using (var request = NewRequest())
        {
            Assert.True((await request.Privileges.SubscribeToPrivilegeAsync(novel.Id, buyer.Id)).Success);
        }

        await Refund(purchase); // 300 - 1000 = -700

        Assert.Equal((0m, 0m), (await Balance(buyer), await Balance(author)));
        var revenue = Assert.Single(await Ledger(author), t => t.Type == TransactionType.PrivilegeRevenue);
        var reversal = Assert.Single(await Ledger(author), t => t.Type == TransactionType.EarningReversed);
        Assert.Equal((-700m, revenue.Id, novel.Id), (reversal.Amount, reversal.ReversedTransactionId!.Value, reversal.NovelId!.Value));
        var held = await Withdrawable(author);
        Assert.Equal((0m, 0m, (DateTime?)null), (held.Withdrawable, held.PendingEarnings, held.NextReleaseAt));
    }

    [Fact]
    public async Task An_author_who_already_spent_a_reversed_earning_has_it_taken_back_from_whom_they_paid()
    {
        var (buyer, author, nextAuthor) = (await SeedUser(), await SeedUser(), await SeedUser());
        var purchase = await BuyOnPlay(buyer);
        Assert.True(await Gift(buyer, await SeedNovel(author), 1000));
        clock.Advance(TimeSpan.FromMinutes(1));
        Assert.True(await Gift(author, await SeedNovel(nextAuthor), 1000)); // the author gives it all away
        clock.Advance(TimeSpan.FromMinutes(1));

        await Refund(purchase);

        // Taking the gift back left the author 1000 below zero, so the author's own gift onward, still on hold, was taken
        // back from the next author in the same way (#27): nobody is left below zero.
        Assert.Equal((0m, 0m, 0m), (await Balance(buyer), await Balance(author), await Balance(nextAuthor)));
        var purchaseId = await PlayPurchaseId(purchase);
        var authorLedger = await Ledger(author);
        var received = Assert.Single(authorLedger, t => t.Type == TransactionType.GiftReceived);
        var reversals = authorLedger.Where(t => t.Type == TransactionType.EarningReversed).OrderBy(t => t.Amount).ToList();
        Assert.Equal([-1000m, 1000m], reversals.Select(t => t.Amount));
        var nextLedger = await Ledger(nextAuthor);
        var nextEarning = Assert.Single(nextLedger, t => t.Type == TransactionType.GiftReceived);
        var nextReversal = Assert.Single(nextLedger, t => t.Type == TransactionType.EarningReversed);
        Assert.Equal(-1000m, nextReversal.Amount);
        // Taken from the author: the gift they received; given back to them: what was taken of the gift they gave on.
        Assert.Equal([received.Id, nextEarning.Id, nextEarning.Id],
            new[] { reversals[0].ReversedTransactionId, reversals[1].ReversedTransactionId, nextReversal.ReversedTransactionId });
        Assert.All(reversals.Append(nextReversal), t => Assert.Equal(purchaseId, t.RelatedRequestId));
        Assert.Equal($"أُلغيت أرباح 1000 نقطة لأن عملية الشراء التي جاءت منها استُرد مبلغها", nextReversal.Description);
        await AssertLedgerAddsUp(buyer);
        await AssertLedgerAddsUp(author);
        await AssertLedgerAddsUp(nextAuthor);
        var next = await Withdrawable(nextAuthor);
        Assert.Equal((0m, 0m), (next.PendingEarnings, next.Withdrawable));
    }

    [Fact]
    public async Task An_author_whose_gift_onward_was_released_already_stays_below_zero_and_cannot_spend()
    {
        var (buyer, author, nextAuthor) = (await SeedUser(), await SeedUser(), await SeedUser());
        var purchase = await BuyOnPlay(buyer);
        Assert.True(await Gift(buyer, await SeedNovel(author), 1000));
        var onward = await SeedNovel(nextAuthor);
        // The author gives it all on while earnings were released at once (a hold of 0 days): nothing to take back there.
        await using (var request = NewRequest())
        {
            await WalletTesting.Wallet(request.Db, clock, holdDays: 0)
                .TransferPointsAsync(author.Id, nextAuthor.Id, 1000, TransactionType.GiftSent, TransactionType.GiftReceived, "هدية", "هدية");
        }
        clock.Advance(TimeSpan.FromMinutes(1));

        await Refund(purchase);

        Assert.Equal((0m, -1000m, 1000m), (await Balance(buyer), await Balance(author), await Balance(nextAuthor)));
        Assert.False(await Gift(author, onward, 1)); // a negative balance refuses every kind of spending
        Assert.Equal(0m, (await Withdrawable(author)).Withdrawable);
        await AssertLedgerAddsUp(author);
        Assert.DoesNotContain(await Ledger(nextAuthor), t => t.Type == TransactionType.EarningReversed);
    }

    [Fact]
    public async Task Refunds_applied_at_the_same_moment_never_take_back_more_than_an_earning()
    {
        var (buyer, author) = (await SeedUser(), await SeedUser());
        var first = await BuyOnPlay(buyer);
        var second = await BuyOnPlay(buyer);
        Assert.True(await Gift(buyer, await SeedNovel(author), 2000)); // one earning of 2000, paid with both

        var applied = await Task.WhenAll(Task.Run(() => ApplyVoid(first)), Task.Run(() => ApplyVoid(second)), Task.Run(() => ApplyVoid(first)));

        Assert.Equal(2, applied.Count(a => a)); // the first one reported twice is applied once
        Assert.Equal((0m, 0m), (await Balance(buyer), await Balance(author)));
        var reversals = (await Ledger(author)).Where(t => t.Type == TransactionType.EarningReversed).ToList();
        Assert.Equal(-2000m, reversals.Sum(t => t.Amount));
        Assert.Equal(2, reversals.Count);
        Assert.Equal(2000m, (await Ledger(buyer)).Where(t => t.Type == TransactionType.EarningReversed).Sum(t => t.Amount));
        await AssertLedgerAddsUp(buyer);
        await AssertLedgerAddsUp(author);
    }
}
