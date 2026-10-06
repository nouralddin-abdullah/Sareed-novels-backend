using Application.Gifts.Commands.SendGift;
using Application.Users;
using Application.Wallet.Commands.ApproveWithdrawal;
using Application.Wallet.Commands.RequestWithdrawal;
using Application.Wallet.DTOs;
using Domain.Constants;
using Domain.Entities;
using Domain.Exceptions;
using Infrastructure.Persistence;
using Infrastructure.PlayBilling;
using Infrastructure.Repositories;
using Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Sareed_novels_backend.Tests.Integration;

/// <summary>
/// A refunded Google Play purchase takes its points back even below zero (the owner's decision), so a negative balance
/// must block every way of spending points (gifts, privilege subscriptions, withdrawals) until it is topped up.
/// </summary>
public class NegativeBalanceTests(SqlServerDatabase database) : IClassFixture<SqlServerDatabase>
{
    private sealed class Request(SqlServerDatabase database, TimeProvider? clock = null) : IAsyncDisposable
    {
        public ApplicationDbContext Db { get; } = database.CreateContext();
        public TransactionManager Transactions => new(Db);
        public TimeProvider Clock { get; } = clock ?? TimeProvider.System;

        public WalletService Wallet => WalletTesting.Wallet(Db, Clock);

        public ValueTask DisposeAsync() => Db.DisposeAsync();
    }

    private static IUserContext SignedIn(User user)
    {
        var context = Substitute.For<IUserContext>();
        context.GetCurrentUser().Returns(new CurrentUser(user.Id, user.Email!, user.UserName!, user.DisplayName));
        return context;
    }

    private async Task<User> SeedUser(decimal? balance)
    {
        var user = Seed.User();
        await using var db = database.CreateContext();
        db.Users.Add(user);
        if (balance is { } b)
        {
            db.UserWallets.Add(new UserWallet { Id = Guid.NewGuid(), UserId = user.Id, CurrentBalance = b });
        }
        await db.SaveChangesAsync();
        return user;
    }

    /// <summary>
    /// A reader who bought 1000 points on Google Play, spent 700 of them, and then got the purchase refunded after the
    /// author's hold on that gift had ended (so the gift stays the author's, #22).
    /// </summary>
    private async Task<(User Reader, User Author, Novel Novel)> ReaderWithRefundedPurchase()
    {
        var reader = await SeedUser(balance: 0m);
        var author = await SeedUser(balance: 0m);
        var novel = Seed.Novel(author, "رواية " + Seed.Marker());
        await using (var db = database.CreateContext())
        {
            db.Novels.Add(novel);
            await db.SaveChangesAsync();
        }

        var google = new FakeGooglePlay();
        var purchase = google.Buy(reader.Id, "points_1000");
        var clock = new MutableClock(DateTime.UtcNow);
        await using (var request = new Request(database, clock))
        {
            var play = PlayBilling(request, google);
            Assert.True((await play.VerifyPurchaseAsync(reader.Id, purchase.ProductId, purchase.Token, purchase.OrderId)).Success);
            await request.Wallet.TransferPointsAsync(reader.Id, author.Id, 700, TransactionType.GiftSent, TransactionType.GiftReceived, "s", "r");
            clock.Advance(TimeSpan.FromDays(31));
            Assert.True(await play.ApplyVoidAsync(new PlayVoidedPurchase(purchase.Token, purchase.OrderId, clock.UtcNow, 1, 0)));
        }

        Assert.Equal(-700m, await Balance(reader.Id));
        return (reader, author, novel);
    }

    private static PlayBillingService PlayBilling(Request request, FakeGooglePlay google) =>
        new(google.Connection(), google.Api(), new PlayPurchaseRepository(request.Db), new UserWalletRepository(request.Db),
            new PointTransactionRepository(request.Db), request.Wallet, request.Transactions, request.Clock, NullLogger<PlayBillingService>.Instance);

    private async Task<decimal?> Balance(string userId)
    {
        await using var db = database.CreateContext();
        return (await db.UserWallets.SingleOrDefaultAsync(w => w.UserId == userId))?.CurrentBalance;
    }

    private async Task<int> LedgerCount(string userId)
    {
        await using var db = database.CreateContext();
        return await db.PointTransactions.CountAsync(t => t.UserId == userId);
    }

    private async Task<Gift> SeedGift(decimal cost)
    {
        var gift = new Gift { Id = Guid.NewGuid(), Name = "Rose", NameAr = "وردة", ImageUrl = "https://example.test/rose.png", Cost = cost };
        await using var db = database.CreateContext();
        db.Gifts.Add(gift);
        await db.SaveChangesAsync();
        return gift;
    }

    private static SendGiftCommandHandler SendGift(Request request, User sender) => new(
        NullLogger<SendGiftCommandHandler>.Instance, new GiftRepository(request.Db), new GiftTransactionRepository(request.Db),
        new NovelsRepository(request.Db), new UserBlocksRepository(request.Db, TimeProvider.System), SignedIn(sender), request.Wallet,
        request.Transactions, new ConfigurationBuilder().Build(), Substitute.For<IServiceScopeFactory>());

    private static PrivilegeService Privileges(Request request) => new(
        NullLogger<PrivilegeService>.Instance, new NovelPrivilegeRepository(request.Db), new PrivilegeSubscriptionRepository(request.Db),
        new NovelsRepository(request.Db), new ChaptersRepository(request.Db), request.Wallet, request.Transactions,
        Substitute.For<IServiceScopeFactory>(), TimeProvider.System);

    [Fact]
    public async Task A_refund_takes_the_points_back_below_zero()
    {
        var (reader, author, _) = await ReaderWithRefundedPurchase();

        Assert.Equal(-700m, await Balance(reader.Id));
        Assert.Equal(700m, await Balance(author.Id));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(100)]
    public async Task A_negative_balance_cannot_send_gifts(decimal cost)
    {
        var (reader, _, novel) = await ReaderWithRefundedPurchase();
        var gift = await SeedGift(cost);
        var ledgerBefore = await LedgerCount(reader.Id);

        await using var request = new Request(database);
        var result = await SendGift(request, reader).Handle(new SendGiftCommand { GiftId = gift.Id, NovelId = novel.Id, Count = 1 }, CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal("رصيدك من النقاط غير كافٍ", result.Message);
        Assert.Equal(-700m, await Balance(reader.Id));
        Assert.Equal(ledgerBefore, await LedgerCount(reader.Id));
    }

    [Fact]
    public async Task A_negative_balance_cannot_subscribe_to_early_access()
    {
        var (reader, _, novel) = await ReaderWithRefundedPurchase();
        await using (var db = database.CreateContext())
        {
            db.NovelPrivileges.Add(new NovelPrivilege
            {
                Id = Guid.NewGuid(), NovelId = novel.Id, IsEnabled = true, SubscriptionCost = 100, CurrentLockedCount = 5, PrivilegeStartSequence = 11
            });
            await db.SaveChangesAsync();
        }

        await using var request = new Request(database);
        var result = await Privileges(request).SubscribeToPrivilegeAsync(novel.Id, reader.Id);

        Assert.False(result.Success);
        Assert.Equal("رصيدك من النقاط غير كافٍ. سعر الاشتراك 100 نقطة.", result.Message);
        Assert.Equal(-700m, await Balance(reader.Id));
        await using var check = database.CreateContext();
        Assert.False(await check.NovelPrivilegeSubscriptions.AnyAsync(s => s.UserId == reader.Id));
    }

    [Fact]
    public async Task A_negative_balance_cannot_request_a_withdrawal()
    {
        var (reader, _, _) = await ReaderWithRefundedPurchase();

        await using var request = new Request(database);
        var result = await new RequestWithdrawalCommandHandler(NullLogger<RequestWithdrawalCommandHandler>.Instance, SignedIn(reader),
                new WithdrawalRequestRepository(request.Db), new PointCalculationService(), request.Wallet, request.Transactions)
            .Handle(new RequestWithdrawalCommand { PointsRequested = 1000, WithdrawalMethod = PaymentMethod.InstaPay, PaymentDetails = "01000000000" },
                CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal("InsufficientWithdrawableBalance", result.Code);
        await using var check = database.CreateContext();
        Assert.False(await check.WithdrawalRequests.AnyAsync(w => w.UserId == reader.Id));
    }

    [Fact]
    public async Task A_withdrawal_requested_before_the_refund_cannot_be_approved_and_stays_pending()
    {
        // An author earns 1500 points in gifts and asks to withdraw 1000; then points bought with a refunded purchase go
        // (after the hold on what they gave away with them, which therefore stays given).
        var author = await SeedUser(balance: 1500m);
        var admin = await SeedUser(balance: null);
        var google = new FakeGooglePlay();
        var purchase = google.Buy(author.Id, "points_2500");
        var withdrawal = new WithdrawalRequest
        {
            Id = Guid.NewGuid(), UserId = author.Id, PointsRequested = 1000, WithdrawalMethod = PaymentMethod.InstaPay,
            PaymentDetails = "01000000000", BaseAmountEGP = 100, TaxDeducted = 10, NetAmountEGP = 90
        };
        await using (var db = database.CreateContext())
        {
            db.PointTransactions.Add(WalletTesting.ReleasedEarning(author.Id, 1500));
            db.WithdrawalRequests.Add(withdrawal);
            await db.SaveChangesAsync();
        }
        var clock = new MutableClock(DateTime.UtcNow);
        await using (var request = new Request(database, clock))
        {
            var play = PlayBilling(request, google);
            Assert.True((await play.VerifyPurchaseAsync(author.Id, purchase.ProductId, purchase.Token, null)).Success); // 4000
            await request.Wallet.TransferPointsAsync(author.Id, admin.Id, 3500, TransactionType.GiftSent, TransactionType.GiftReceived, "s", "r"); // 500
            clock.Advance(TimeSpan.FromDays(31));
            await play.ApplyVoidAsync(new PlayVoidedPurchase(purchase.Token, purchase.OrderId, clock.UtcNow, 1, 0)); // -2000
        }
        Assert.Equal(-2000m, await Balance(author.Id));

        await using (var request = new Request(database, clock))
        {
            var result = await new ApproveWithdrawalCommandHandler(NullLogger<ApproveWithdrawalCommandHandler>.Instance, SignedIn(admin),
                    new WithdrawalRequestRepository(request.Db), request.Wallet, request.Transactions)
                .Handle(new ApproveWithdrawalCommand { RequestId = withdrawal.Id }, CancellationToken.None);
            Assert.False(result.Success);
        }

        Assert.Equal(-2000m, await Balance(author.Id));
        await using var check = database.CreateContext();
        Assert.Equal(RequestStatus.Pending, (await check.WithdrawalRequests.SingleAsync(w => w.Id == withdrawal.Id)).Status);
    }

    [Theory]
    [InlineData(-0.01)]
    [InlineData(-700)]
    public async Task The_wallet_refuses_any_debit_from_a_negative_balance(decimal balance)
    {
        var user = await SeedUser(balance);
        var other = await SeedUser(balance: 0m);

        await using var request = new Request(database);
        Assert.False(await request.Wallet.HasSufficientBalanceAsync(user.Id, 0.01m));
        await Assert.ThrowsAsync<InsufficientBalanceException>(() =>
            request.Wallet.DeductPointsAsync(user.Id, 0.01m, TransactionType.WithdrawalApproved, "w"));
        await Assert.ThrowsAsync<InsufficientBalanceException>(() =>
            request.Wallet.TransferPointsAsync(user.Id, other.Id, 0.01m, TransactionType.GiftSent, TransactionType.GiftReceived, "s", "r"));

        Assert.Equal(balance, await Balance(user.Id));
        Assert.Equal(0m, await Balance(other.Id));
        Assert.Equal(0, await LedgerCount(user.Id));
    }

    [Fact]
    public async Task Topping_up_past_the_debt_allows_spending_again()
    {
        var (reader, _, novel) = await ReaderWithRefundedPurchase(); // -700
        var gift = await SeedGift(100);

        var google = new FakeGooglePlay();
        var topUp = google.Buy(reader.Id, "points_500");
        await using (var request = new Request(database))
        {
            var result = await PlayBilling(request, google).VerifyPurchaseAsync(reader.Id, topUp.ProductId, topUp.Token, null);
            Assert.Equal((true, 500, -200m), (result.Success, result.PointsAdded, result.CurrentBalance));

            var stillNegative = await SendGift(request, reader).Handle(new SendGiftCommand { GiftId = gift.Id, NovelId = novel.Id }, CancellationToken.None);
            Assert.False(stillNegative.Success);
        }

        await using (var request = new Request(database))
        {
            await request.Wallet.AddPointsAsync(reader.Id, 300, TransactionType.RechargeApproved, "recharge"); // 100
            var sent = await SendGift(request, reader).Handle(new SendGiftCommand { GiftId = gift.Id, NovelId = novel.Id }, CancellationToken.None);
            Assert.True(sent.Success, sent.Message);
        }

        Assert.Equal(0m, await Balance(reader.Id));
    }
}
