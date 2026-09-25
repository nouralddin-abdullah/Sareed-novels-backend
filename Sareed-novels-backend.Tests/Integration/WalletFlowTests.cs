using Application.Competitions.Commands.FinalizeCompetition;
using Application.Competitions.DTOs;
using Application.Gifts.Commands.SendGift;
using Application.Users;
using Application.Wallet.Commands.ApproveRecharge;
using Application.Wallet.Commands.ApproveWithdrawal;
using Application.Wallet.Commands.RejectRecharge;
using AutoMapper;
using Domain.Constants;
using Domain.Entities;
using Domain.Exceptions;
using Infrastructure.Persistence;
using Infrastructure.Repositories;
using Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Sareed_novels_backend.Tests.Integration;

/// <summary>
/// Money flows against a real SQL Server, including parallel requests. Each simulated request gets its own DbContext
/// and scoped services, as in the API.
/// </summary>
public class WalletFlowTests(SqlServerDatabase database) : IClassFixture<SqlServerDatabase>
{
    private sealed class Request(SqlServerDatabase database) : IAsyncDisposable
    {
        public ApplicationDbContext Db { get; } = database.CreateContext();
        public TransactionManager Transactions => new(Db);

        public WalletService Wallet => new(NullLogger<WalletService>.Instance, new UserWalletRepository(Db),
            new PointTransactionRepository(Db), null!, Transactions);

        public ValueTask DisposeAsync() => Db.DisposeAsync();
    }

    private static IUserContext SignedIn(User user)
    {
        var context = Substitute.For<IUserContext>();
        context.GetCurrentUser().Returns(new CurrentUser(user.Id, user.Email!, user.UserName!, user.DisplayName));
        return context;
    }

    private async Task<User[]> SeedUsers(params decimal?[] balances)
    {
        var users = balances.Select(_ => Seed.User()).ToArray();
        await using var db = database.CreateContext();
        db.Users.AddRange(users);
        for (var i = 0; i < users.Length; i++)
        {
            if (balances[i] is { } balance)
            {
                db.UserWallets.Add(new UserWallet { Id = Guid.NewGuid(), UserId = users[i].Id, CurrentBalance = balance });
            }
        }
        await db.SaveChangesAsync();
        return users;
    }

    private async Task<decimal?> Balance(string userId)
    {
        await using var db = database.CreateContext();
        return (await db.UserWallets.SingleOrDefaultAsync(w => w.UserId == userId))?.CurrentBalance;
    }

    private async Task<List<PointTransaction>> Ledger(string userId)
    {
        await using var db = database.CreateContext();
        return await db.PointTransactions.Where(t => t.UserId == userId).OrderBy(t => t.BalanceAfter).ToListAsync();
    }

    private static Task<T[]> Parallel<T>(int count, Func<Task<T>> action) =>
        Task.WhenAll(Enumerable.Range(0, count).Select(_ => Task.Run(action)));

    // ===== Wallet service =====

    [Fact]
    public async Task Parallel_transfers_cannot_spend_the_same_points_twice()
    {
        // Before the fix: 10 of 10 succeeded, the sender kept 700 and the author's ledger said 3000 while its wallet said 300.
        var users = await SeedUsers(1000m, 0m);
        var (sender, author) = (users[0], users[1]);

        var results = await Parallel(10, async () =>
        {
            await using var request = new Request(database);
            try
            {
                await request.Wallet.TransferPointsAsync(sender.Id, author.Id, 300, TransactionType.GiftSent,
                    TransactionType.GiftReceived, "sent", "received");
                return true;
            }
            catch (InsufficientBalanceException)
            {
                return false;
            }
        });

        Assert.Equal(3, results.Count(ok => ok));
        Assert.Equal(100m, await Balance(sender.Id));
        Assert.Equal(900m, await Balance(author.Id));

        var sent = await Ledger(sender.Id);
        Assert.Equal([100m, 400m, 700m], sent.Select(t => t.BalanceAfter));
        Assert.All(sent, t => Assert.Equal(t.BalanceBefore + t.Amount, t.BalanceAfter));
        Assert.Equal(900m, (await Ledger(author.Id)).Sum(t => t.Amount));
    }

    [Fact]
    public async Task Opposite_transfers_at_the_same_time_all_complete()
    {
        var users = await SeedUsers(1000m, 1000m);
        var (a, b) = (users[0], users[1]);

        await Parallel(10, async () =>
        {
            await using var request = new Request(database);
            await request.Wallet.TransferPointsAsync(a.Id, b.Id, 10, TransactionType.GiftSent, TransactionType.GiftReceived, "s", "r");
            await request.Wallet.TransferPointsAsync(b.Id, a.Id, 10, TransactionType.GiftSent, TransactionType.GiftReceived, "s", "r");
            return true;
        });

        Assert.Equal(1000m, await Balance(a.Id));
        Assert.Equal(1000m, await Balance(b.Id));
    }

    [Fact]
    public async Task A_debit_the_balance_does_not_cover_changes_nothing()
    {
        var user = (await SeedUsers(50m))[0];

        await using var request = new Request(database);
        await Assert.ThrowsAsync<InsufficientBalanceException>(() =>
            request.Wallet.DeductPointsAsync(user.Id, 50.01m, TransactionType.WithdrawalApproved, "w"));

        Assert.Equal(50m, await Balance(user.Id));
        Assert.Empty(await Ledger(user.Id));
    }

    [Fact]
    public async Task First_credits_to_a_new_user_create_exactly_one_wallet()
    {
        var user = (await SeedUsers(balances: [null]))[0];

        await Parallel(5, async () =>
        {
            await using var request = new Request(database);
            await request.Wallet.AddPointsAsync(user.Id, 100, TransactionType.RechargeApproved, "r");
            return true;
        });

        await using var db = database.CreateContext();
        Assert.Equal(1, await db.UserWallets.CountAsync(w => w.UserId == user.Id));
        Assert.Equal(500m, await Balance(user.Id));
        Assert.Equal([100m, 200m, 300m, 400m, 500m], (await Ledger(user.Id)).Select(t => t.BalanceAfter));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public async Task Amounts_must_be_positive(decimal amount)
    {
        var users = await SeedUsers(100m, 100m);
        var (a, b) = (users[0], users[1]);
        await using var request = new Request(database);

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => request.Wallet.AddPointsAsync(a.Id, amount, "t", "d"));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => request.Wallet.DeductPointsAsync(a.Id, amount, "t", "d"));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            request.Wallet.TransferPointsAsync(a.Id, b.Id, amount, "t", "t", "d", "d"));
        Assert.Equal(100m, await Balance(a.Id));
        Assert.Equal(100m, await Balance(b.Id));
    }

    [Fact]
    public async Task Points_cannot_be_transferred_to_yourself()
    {
        var user = (await SeedUsers(100m))[0];
        await using var request = new Request(database);

        await Assert.ThrowsAsync<ArgumentException>(() =>
            request.Wallet.TransferPointsAsync(user.Id, user.Id, 10, "t", "t", "d", "d"));
    }

    // ===== Gifts =====

    private async Task<(Gift Gift, Novel Novel)> SeedGiftAndNovel(User author, decimal cost)
    {
        var gift = new Gift { Id = Guid.NewGuid(), Name = "Crown", ImageUrl = "https://example.test/crown.png", Cost = cost };
        var novel = Seed.Novel(author, "رواية " + Seed.Marker());
        await using var db = database.CreateContext();
        db.Gifts.Add(gift);
        db.Novels.Add(novel);
        await db.SaveChangesAsync();
        return (gift, novel);
    }

    private static SendGiftCommandHandler SendGift(Request request, User sender) => new(
        NullLogger<SendGiftCommandHandler>.Instance,
        new GiftRepository(request.Db),
        new GiftTransactionRepository(request.Db),
        new NovelsRepository(request.Db),
        SignedIn(sender),
        request.Wallet,
        request.Transactions,
        Substitute.For<IServiceScopeFactory>());

    [Fact]
    public async Task Parallel_gifts_are_paid_for_exactly_once_each()
    {
        var users = await SeedUsers(1000m, null);
        var (sender, author) = (users[0], users[1]);
        var (gift, novel) = await SeedGiftAndNovel(author, cost: 300);

        var results = await Parallel(8, async () =>
        {
            await using var request = new Request(database);
            return await SendGift(request, sender).Handle(
                new SendGiftCommand { GiftId = gift.Id, NovelId = novel.Id, Count = 1 }, CancellationToken.None);
        });

        Assert.Equal(3, results.Count(r => r.Success));
        Assert.All(results.Where(r => !r.Success), r => Assert.Equal("Insufficient points balance", r.Message));
        Assert.Equal(100m, await Balance(sender.Id));
        Assert.Equal(900m, await Balance(author.Id));

        await using var db = database.CreateContext();
        var records = await db.GiftTransactions.Where(t => t.NovelId == novel.Id).ToListAsync();
        Assert.Equal(3, records.Count);
        Assert.Equal(900m, records.Sum(t => t.TotalCost));
    }

    [Fact]
    public async Task A_failed_gift_record_rolls_the_payment_back()
    {
        var users = await SeedUsers(1000m, 0m);
        var (sender, author) = (users[0], users[1]);
        var (gift, novel) = await SeedGiftAndNovel(author, cost: 100);

        await using var request = new Request(database);
        var failingRecords = Substitute.For<Domain.Repositories.IGiftTransactionRepository>();
        failingRecords.CreateTransaction(Arg.Any<GiftTransaction>()).Returns<GiftTransaction>(_ => throw new DbUpdateException("boom"));
        var handler = new SendGiftCommandHandler(NullLogger<SendGiftCommandHandler>.Instance, new GiftRepository(request.Db),
            failingRecords, new NovelsRepository(request.Db), SignedIn(sender), request.Wallet, request.Transactions,
            Substitute.For<IServiceScopeFactory>());

        var result = await handler.Handle(new SendGiftCommand { GiftId = gift.Id, NovelId = novel.Id, Count = 2 }, CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal(1000m, await Balance(sender.Id));
        Assert.Equal(0m, await Balance(author.Id));
        Assert.Empty(await Ledger(sender.Id));
    }

    // ===== Recharge and withdrawal approval =====

    private async Task<RechargeRequest> SeedRecharge(User user, int points)
    {
        var recharge = new RechargeRequest
        {
            Id = Guid.NewGuid(), UserId = user.Id, PointsRequested = points, PaymentMethod = PaymentMethod.VodafoneCash,
            BaseAmountEGP = points * 0.1m, TransactionFee = points * 0.01m, TotalAmountEGP = points * 0.11m
        };
        await using var db = database.CreateContext();
        db.RechargeRequests.Add(recharge);
        await db.SaveChangesAsync();
        return recharge;
    }

    private async Task<WithdrawalRequest> SeedWithdrawal(User user, int points)
    {
        var withdrawal = new WithdrawalRequest
        {
            Id = Guid.NewGuid(), UserId = user.Id, PointsRequested = points, WithdrawalMethod = PaymentMethod.InstaPay,
            PaymentDetails = "01000000000", BaseAmountEGP = points * 0.1m, TaxDeducted = points * 0.01m, NetAmountEGP = points * 0.09m
        };
        await using var db = database.CreateContext();
        db.WithdrawalRequests.Add(withdrawal);
        await db.SaveChangesAsync();
        return withdrawal;
    }

    [Fact]
    public async Task A_recharge_approved_twice_at_once_is_credited_once()
    {
        var users = await SeedUsers(null, null);
        var (user, admin) = (users[0], users[1]);
        var recharge = await SeedRecharge(user, 500);

        var results = await Parallel(5, async () =>
        {
            await using var request = new Request(database);
            return await new ApproveRechargeCommandHandler(NullLogger<ApproveRechargeCommandHandler>.Instance, SignedIn(admin),
                    new RechargeRequestRepository(request.Db), request.Wallet, request.Transactions)
                .Handle(new ApproveRechargeCommand { RequestId = recharge.Id }, CancellationToken.None);
        });

        Assert.Single(results, r => r.Success);
        Assert.Equal(500m, await Balance(user.Id));
        Assert.Single(await Ledger(user.Id));

        await using var db = database.CreateContext();
        var stored = await db.RechargeRequests.SingleAsync(r => r.Id == recharge.Id);
        Assert.Equal(RequestStatus.Approved, stored.Status);
        Assert.Equal(admin.Id, stored.ProcessedBy);
    }

    [Fact]
    public async Task An_approved_recharge_cannot_then_be_rejected()
    {
        var users = await SeedUsers(null, null);
        var (user, admin) = (users[0], users[1]);
        var recharge = await SeedRecharge(user, 500);

        await using (var request = new Request(database))
        {
            var approved = await new ApproveRechargeCommandHandler(NullLogger<ApproveRechargeCommandHandler>.Instance,
                    SignedIn(admin), new RechargeRequestRepository(request.Db), request.Wallet, request.Transactions)
                .Handle(new ApproveRechargeCommand { RequestId = recharge.Id }, CancellationToken.None);
            Assert.True(approved.Success);
        }

        await using (var request = new Request(database))
        {
            var repository = new RechargeRequestRepository(request.Db);
            Assert.False(await repository.TryMarkProcessedAsync(recharge.Id, RequestStatus.Rejected, admin.Id, "late"));

            var rejected = await new RejectRechargeCommandHandler(NullLogger<RejectRechargeCommandHandler>.Instance,
                    SignedIn(admin), repository)
                .Handle(new RejectRechargeCommand { RequestId = recharge.Id, RejectionReason = "late" }, CancellationToken.None);
            Assert.False(rejected.Success);
        }

        await using var db = database.CreateContext();
        Assert.Equal(RequestStatus.Approved, (await db.RechargeRequests.SingleAsync(r => r.Id == recharge.Id)).Status);
        Assert.Equal(500m, await Balance(user.Id));
    }

    [Fact]
    public async Task Of_two_withdrawals_for_the_whole_balance_only_one_is_paid_and_the_other_stays_pending()
    {
        var users = await SeedUsers(1000m, null);
        var (user, admin) = (users[0], users[1]);
        var first = await SeedWithdrawal(user, 1000);
        var second = await SeedWithdrawal(user, 1000);

        var results = await Task.WhenAll(new[] { first, second, first, second }.Select(w => Task.Run(async () =>
        {
            await using var request = new Request(database);
            return await new ApproveWithdrawalCommandHandler(NullLogger<ApproveWithdrawalCommandHandler>.Instance, SignedIn(admin),
                    new WithdrawalRequestRepository(request.Db), request.Wallet, request.Transactions)
                .Handle(new ApproveWithdrawalCommand { RequestId = w.Id }, CancellationToken.None);
        })));

        Assert.Single(results, r => r.Success);
        Assert.Equal(0m, await Balance(user.Id));
        Assert.Single(await Ledger(user.Id));

        await using var db = database.CreateContext();
        var statuses = await db.WithdrawalRequests.Where(w => w.UserId == user.Id).Select(w => w.Status).ToListAsync();
        Assert.Equal([RequestStatus.Approved, RequestStatus.Pending], statuses.Order());
    }

    // ===== Privilege subscriptions =====

    [Fact]
    public async Task Subscribing_twice_at_once_charges_once()
    {
        var users = await SeedUsers(1000m, null);
        var (reader, author) = (users[0], users[1]);
        var novel = Seed.Novel(author, "رواية " + Seed.Marker());
        await using (var db = database.CreateContext())
        {
            db.Novels.Add(novel);
            db.NovelPrivileges.Add(new NovelPrivilege
            {
                Id = Guid.NewGuid(), NovelId = novel.Id, IsEnabled = true, SubscriptionCost = 300,
                CurrentLockedCount = 5, PrivilegeStartSequence = 11
            });
            await db.SaveChangesAsync();
        }

        var results = await Parallel(5, async () =>
        {
            await using var request = new Request(database);
            var privileges = new PrivilegeService(NullLogger<PrivilegeService>.Instance, new NovelPrivilegeRepository(request.Db),
                new PrivilegeSubscriptionRepository(request.Db), new NovelsRepository(request.Db), new ChaptersRepository(request.Db),
                request.Wallet, request.Transactions, Substitute.For<IServiceScopeFactory>());
            return await privileges.SubscribeToPrivilegeAsync(novel.Id, reader.Id);
        });

        Assert.Single(results, r => r.Success);
        Assert.Equal(700m, await Balance(reader.Id));
        Assert.Equal(300m, await Balance(author.Id));

        await using var check = database.CreateContext();
        Assert.Equal(1, await check.NovelPrivilegeSubscriptions.CountAsync(s => s.NovelId == novel.Id && s.UserId == reader.Id));
    }

    // ===== Competitions =====

    [Fact]
    public async Task Finalizing_twice_at_once_awards_one_set_of_winners()
    {
        var authors = await SeedUsers(null, null, null, null);
        var competition = new Competition
        {
            Id = Guid.NewGuid(), Name = "مسابقة", Slug = "c-" + Seed.Marker(), Status = CompetitionStatus.Judging,
            PrizeFirstPlace = 40, PrizeSecondPlace = 35, PrizeThirdPlace = 25,
            ParticipationStartDate = DateTime.UtcNow.AddDays(-30), ParticipationEndDate = DateTime.UtcNow.AddDays(-20),
            JudgmentStartDate = DateTime.UtcNow.AddDays(-10), JudgmentEndDate = DateTime.UtcNow.AddDays(-1), ResultsDate = DateTime.UtcNow
        };
        await using (var db = database.CreateContext())
        {
            db.Competitions.Add(competition);
            for (var i = 0; i < authors.Length; i++)
            {
                var novel = Seed.Novel(authors[i], "رواية " + Seed.Marker());
                db.Novels.Add(novel);
                db.CompetitionParticipants.Add(new CompetitionParticipant
                {
                    Id = Guid.NewGuid(), CompetitionId = competition.Id, NovelId = novel.Id, CurrentPoints = 10 * (i + 1)
                });
            }
            await db.SaveChangesAsync();
        }

        var mapper = Substitute.For<IMapper>();
        mapper.Map<List<CompetitionWinnerDto>>(Arg.Any<object>())
            .Returns(call => ((IEnumerable<CompetitionWinner>)call[0]).Select(w => new CompetitionWinnerDto { Rank = w.Rank }).ToList());

        var results = await Parallel(3, async () =>
        {
            await using var request = new Request(database);
            return await new FinalizeCompetitionCommandHandler(new CompetitionRepository(request.Db),
                    new CompetitionParticipantRepository(request.Db), new CompetitionWinnerRepository(request.Db),
                    request.Transactions, mapper)
                .Handle(new FinalizeCompetitionCommand { CompetitionId = competition.Id }, CancellationToken.None);
        });

        Assert.All(results, winners => Assert.Equal([1, 2, 3], winners.Select(w => w.Rank)));
        await using var check = database.CreateContext();
        Assert.Equal(3, await check.CompetitionWinners.CountAsync(w => w.CompetitionId == competition.Id));
        Assert.Equal(CompetitionStatus.Completed, (await check.Competitions.SingleAsync(c => c.Id == competition.Id)).Status);
    }
}
