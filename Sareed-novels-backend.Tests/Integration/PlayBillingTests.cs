using System.Net;
using Application.Wallet.DTOs;
using Domain.Constants;
using Domain.Entities;
using Infrastructure.BackgroundJobs;
using Infrastructure.Persistence;
using Infrastructure.PlayBilling;
using Infrastructure.Repositories;
using Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Sareed_novels_backend.Tests.Integration;

/// <summary>
/// Google Play point packs end to end against a real SQL Server and a fake Google Play Developer API: verification,
/// crediting once per token (also under concurrency), ownership, consuming and its retries, and voided purchases.
/// Each simulated request gets its own DbContext, as in the API.
/// </summary>
public class PlayBillingTests(SqlServerDatabase database) : IClassFixture<SqlServerDatabase>, IAsyncLifetime
{
    private readonly FakeGooglePlay google = new();
    private readonly MutableClock clock = new(DateTime.UtcNow);

    public async Task InitializeAsync()
    {
        // The database is shared by the class: each test starts with no voided-purchases cursor and no consume owed by
        // an earlier test's purchases (the fake Google Play is new for each test).
        await using var db = database.CreateContext();
        await db.PlaySyncCursors.ExecuteDeleteAsync();
        await db.PlayPurchases.ExecuteUpdateAsync(s => s.SetProperty(p => p.NextConsumeAttemptAt, (DateTime?)null));
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private sealed class Request(SqlServerDatabase database, FakeGooglePlay google, PlayBillingConnection connection, TimeProvider clock)
        : IAsyncDisposable
    {
        public ApplicationDbContext Db { get; } = database.CreateContext();

        public PlayBillingService Service => new(connection, google.Api(connection), new PlayPurchaseRepository(Db),
            new UserWalletRepository(Db), new PointTransactionRepository(Db), WalletTesting.Wallet(Db, clock), new TransactionManager(Db), clock,
            NullLogger<PlayBillingService>.Instance);

        public ValueTask DisposeAsync() => Db.DisposeAsync();
    }

    private Request NewRequest(PlayBillingConnection? connection = null) => new(database, google, connection ?? google.Connection(), clock);

    private async Task<PlayPurchaseResult> Verify(string userId, FakePlayPurchase purchase, string? productId = null,
        PlayBillingConnection? connection = null)
    {
        await using var request = NewRequest(connection);
        return await request.Service.VerifyPurchaseAsync(userId, productId ?? purchase.ProductId, purchase.Token, purchase.OrderId);
    }

    private async Task<User> SeedUser(decimal? balance = null)
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

    private async Task<decimal?> Balance(string userId)
    {
        await using var db = database.CreateContext();
        return (await db.UserWallets.SingleOrDefaultAsync(w => w.UserId == userId))?.CurrentBalance;
    }

    private async Task<List<PointTransaction>> Ledger(string userId)
    {
        await using var db = database.CreateContext();
        return await db.PointTransactions.Where(t => t.UserId == userId).OrderBy(t => t.CreatedAt).ThenBy(t => t.Type).ToListAsync();
    }

    private async Task<PlayPurchase?> Stored(string token)
    {
        await using var db = database.CreateContext();
        return await db.PlayPurchases.SingleOrDefaultAsync(p => p.PurchaseToken == token);
    }

    private async Task<int> SyncVoided()
    {
        await using var request = NewRequest();
        return await request.Service.SyncVoidedPurchasesAsync(CancellationToken.None);
    }

    private async Task<int> RetryConsumes()
    {
        await using var request = NewRequest();
        return await request.Service.RetryConsumptionsAsync(CancellationToken.None);
    }

    private static Task<T[]> Parallel<T>(int count, Func<Task<T>> action) =>
        Task.WhenAll(Enumerable.Range(0, count).Select(_ => Task.Run(action)));

    // ===== Verifying and crediting =====

    [Fact]
    public async Task A_purchase_is_verified_credited_once_recorded_and_consumed_by_the_server()
    {
        var user = await SeedUser(balance: 450m);
        var purchase = google.Buy(user.Id, "points_1000");

        var result = await Verify(user.Id, purchase);

        Assert.True(result.Success, result.Code);
        Assert.Equal(1000, result.PointsAdded);
        Assert.Equal(1450m, result.CurrentBalance);
        Assert.Equal(1450m, await Balance(user.Id));

        var entry = Assert.Single(await Ledger(user.Id));
        Assert.Equal(TransactionType.PlayPurchase, entry.Type);
        Assert.Equal((1000m, 450m, 1450m), (entry.Amount, entry.BalanceBefore, entry.BalanceAfter));
        Assert.Equal($"شراء 1000 نقطة عبر Google Play (رقم الطلب {purchase.OrderId})", entry.Description);

        var stored = await Stored(purchase.Token);
        Assert.NotNull(stored);
        Assert.Equal((PlayPurchaseStatus.Credited, user.Id, "points_1000", purchase.OrderId, 1000),
            (stored.Status, stored.UserId, stored.ProductId, stored.OrderId, stored.Points));
        Assert.Equal(entry.RelatedRequestId, stored.Id);
        Assert.False(stored.IsTestPurchase);
        Assert.NotNull(stored.PurchasedAt);

        // The server consumed it (the app must not), so nothing is owed and the pack can be bought again.
        Assert.NotNull(stored.ConsumedAt);
        Assert.Null(stored.NextConsumeAttemptAt);
        Assert.Equal(1, google.Purchases[purchase.Token].ConsumptionState);
        var calls = google.Requests.ToList();
        Assert.Equal(["GET", "POST"], calls.Select(r => r.Method));
        Assert.All(calls, r => Assert.Equal($"Bearer {FakeGooglePlay.AccessToken}", r.Authorization));
        Assert.Equal($"/androidpublisher/v3/applications/com.sardnovels.app/purchases/products/points_1000/tokens/{purchase.Token}:consume", calls[1].Path);
    }

    [Fact]
    public async Task A_first_purchase_creates_the_wallet()
    {
        var user = await SeedUser(balance: null);

        var result = await Verify(user.Id, google.Buy(user.Id, "points_500"));

        Assert.True(result.Success, result.Code);
        Assert.Equal((500, 500m), (result.PointsAdded, result.CurrentBalance));
        Assert.Equal(500m, await Balance(user.Id));
    }

    [Fact]
    public async Task Replays_by_the_same_user_get_the_same_answer_and_credit_once_without_asking_Google_again()
    {
        var user = await SeedUser(balance: 0m);
        var purchase = google.Buy(user.Id, "points_2500");

        var first = await Verify(user.Id, purchase);
        var second = await Verify(user.Id, purchase);
        var third = await Verify(user.Id, purchase);

        Assert.All([first, second, third], r => Assert.Equal((true, 2500, 2500m), (r.Success, r.PointsAdded, r.CurrentBalance)));
        Assert.Equal(2500m, await Balance(user.Id));
        Assert.Single(await Ledger(user.Id));
        Assert.Equal(1, google.Count("GET", purchase.Token));
    }

    [Fact]
    public async Task Concurrent_replays_credit_the_purchase_once()
    {
        var user = await SeedUser(balance: 100m);
        var purchase = google.Buy(user.Id, "points_1000");

        var results = await Parallel(10, () => Verify(user.Id, purchase));

        Assert.All(results, r => Assert.Equal((true, 1000), (r.Success, r.PointsAdded)));
        Assert.Equal(1100m, await Balance(user.Id));
        var entry = Assert.Single(await Ledger(user.Id));
        Assert.Equal((100m, 1100m), (entry.BalanceBefore, entry.BalanceAfter));
        await using var db = database.CreateContext();
        Assert.Equal(1, await db.PlayPurchases.CountAsync(p => p.PurchaseToken == purchase.Token));
    }

    [Fact]
    public async Task A_token_already_credited_to_one_user_is_refused_for_everyone_else()
    {
        var owner = await SeedUser(balance: 0m);
        var other = await SeedUser(balance: 0m);
        var purchase = google.Buy(owner.Id);
        Assert.True((await Verify(owner.Id, purchase)).Success);

        var stolen = await Verify(other.Id, purchase);

        Assert.Equal(PlayPurchaseError.AlreadyUsedByAnotherUser, stolen.Error);
        Assert.Equal("AlreadyUsedByAnotherUser", stolen.Code);
        Assert.Equal(1000m, await Balance(owner.Id));
        Assert.Equal(0m, await Balance(other.Id));
        Assert.Empty(await Ledger(other.Id));
    }

    [Fact]
    public async Task A_purchase_made_for_another_account_is_refused_and_its_owner_can_still_claim_it()
    {
        var owner = await SeedUser(balance: 0m);
        var thief = await SeedUser(balance: 0m);
        var purchase = google.Buy(owner.Id);

        var stolen = await Verify(thief.Id, purchase);

        Assert.Equal(PlayPurchaseError.AccountMismatch, stolen.Error);
        Assert.Null(await Stored(purchase.Token));
        Assert.Equal(0m, await Balance(thief.Id));

        var claimed = await Verify(owner.Id, purchase);
        Assert.Equal((true, 1000), (claimed.Success, claimed.PointsAdded));
    }

    [Fact]
    public async Task A_purchase_without_an_account_id_is_refused()
    {
        var user = await SeedUser(balance: 0m);
        var purchase = google.Buy(user.Id, change: p => p.ObfuscatedAccountId = null);

        var result = await Verify(user.Id, purchase);

        Assert.Equal(PlayPurchaseError.AccountMismatch, result.Error);
        Assert.Null(await Stored(purchase.Token));
        Assert.Equal(0, google.Count("POST", ":consume"));
    }

    [Theory]
    [InlineData(2, PlayPurchaseError.PurchasePending)]
    [InlineData(1, PlayPurchaseError.PurchaseCanceled)]
    public async Task Purchases_that_are_not_completed_are_not_credited(int purchaseState, PlayPurchaseError expected)
    {
        var user = await SeedUser(balance: 0m);
        var purchase = google.Buy(user.Id, change: p => p.PurchaseState = purchaseState);

        var result = await Verify(user.Id, purchase);

        Assert.Equal(expected, result.Error);
        Assert.Null(await Stored(purchase.Token));
        Assert.Equal(0m, await Balance(user.Id));
        Assert.Equal(0, google.Count("POST", ":consume"));
    }

    [Fact]
    public async Task A_pending_purchase_is_credited_once_Google_reports_it_purchased()
    {
        var user = await SeedUser(balance: 0m);
        var purchase = google.Buy(user.Id, change: p => p.PurchaseState = 2);
        Assert.Equal(PlayPurchaseError.PurchasePending, (await Verify(user.Id, purchase)).Error);

        purchase.PurchaseState = 0;
        var result = await Verify(user.Id, purchase);

        Assert.Equal((true, 1000), (result.Success, result.PointsAdded));
    }

    [Fact]
    public async Task Unknown_products_are_refused_without_asking_Google()
    {
        var user = await SeedUser(balance: 0m);
        var purchase = google.Buy(user.Id, "points_999999");

        var result = await Verify(user.Id, purchase);

        Assert.Equal(PlayPurchaseError.UnknownProduct, result.Error);
        Assert.Empty(google.Requests);
    }

    [Fact]
    public async Task A_purchase_of_another_product_is_refused()
    {
        var user = await SeedUser(balance: 0m);
        var purchase = google.Buy(user.Id, "points_500");

        var result = await Verify(user.Id, purchase, productId: "points_5000");

        Assert.Equal(PlayPurchaseError.ProductMismatch, result.Error);
        Assert.Null(await Stored(purchase.Token));
        Assert.Equal(0m, await Balance(user.Id));
    }

    [Theory]
    [InlineData("", "tok")]
    [InlineData("points_1000", "")]
    [InlineData("points_1000", "   ")]
    [InlineData(null, "tok")]
    [InlineData("points_1000", null)]
    public async Task Missing_fields_are_an_invalid_request(string? productId, string? token)
    {
        var user = await SeedUser(balance: 0m);
        await using var request = NewRequest();

        var result = await request.Service.VerifyPurchaseAsync(user.Id, productId, token, null);

        Assert.Equal(PlayPurchaseError.InvalidRequest, result.Error);
        Assert.Empty(google.Requests);
    }

    [Fact]
    public async Task Overlong_tokens_are_an_invalid_request()
    {
        var user = await SeedUser(balance: 0m);
        await using var request = NewRequest();

        var result = await request.Service.VerifyPurchaseAsync(user.Id, "points_1000", new string('a', PlayPurchase.PurchaseTokenMaxLength + 1), null);

        Assert.Equal(PlayPurchaseError.InvalidRequest, result.Error);
    }

    [Fact]
    public async Task Tokens_Google_does_not_know_are_refused()
    {
        var user = await SeedUser(balance: 0m);
        await using var request = NewRequest();

        var result = await request.Service.VerifyPurchaseAsync(user.Id, "points_1000", "made-up-token", null);

        Assert.Equal(PlayPurchaseError.PurchaseNotFound, result.Error);
        Assert.Null(await Stored("made-up-token"));
    }

    [Fact]
    public async Task License_test_purchases_are_refused_unless_allowed_by_configuration()
    {
        var user = await SeedUser(balance: 0m);
        var purchase = google.Buy(user.Id, change: p => p.PurchaseType = 0);

        var refused = await Verify(user.Id, purchase);
        Assert.Equal(PlayPurchaseError.TestPurchaseNotAllowed, refused.Error);
        Assert.Null(await Stored(purchase.Token));

        var allowed = await Verify(user.Id, purchase, connection: google.Connection(allowTestPurchases: true));
        Assert.Equal((true, 1000), (allowed.Success, allowed.PointsAdded));
        Assert.True((await Stored(purchase.Token))!.IsTestPurchase);
    }

    [Theory]
    [InlineData(1)] // promo code
    [InlineData(2)] // rewarded
    public async Task Promo_code_and_rewarded_purchases_are_credited(int purchaseType)
    {
        var user = await SeedUser(balance: 0m);

        var result = await Verify(user.Id, google.Buy(user.Id, change: p => p.PurchaseType = purchaseType));

        Assert.True(result.Success, result.Code);
    }

    [Fact]
    public async Task Multi_quantity_purchases_are_refused_and_left_for_Google_to_refund()
    {
        var user = await SeedUser(balance: 0m);
        var purchase = google.Buy(user.Id, change: p => p.Quantity = 3);

        var result = await Verify(user.Id, purchase);

        Assert.Equal(PlayPurchaseError.UnsupportedQuantity, result.Error);
        Assert.Null(await Stored(purchase.Token));
        Assert.Equal(0, google.Purchases[purchase.Token].ConsumptionState);
    }

    [Fact]
    public async Task An_explicit_quantity_of_one_is_fine()
    {
        var user = await SeedUser(balance: 0m);

        var result = await Verify(user.Id, google.Buy(user.Id, change: p => p.Quantity = 1));

        Assert.True(result.Success, result.Code);
    }

    [Theory]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    [InlineData(HttpStatusCode.TooManyRequests)]
    public async Task When_Google_fails_nothing_is_credited_and_a_retry_works(HttpStatusCode status)
    {
        var user = await SeedUser(balance: 0m);
        var purchase = google.Buy(user.Id);
        google.Intercept = _ => FakeGooglePlay.Error(status, "backendError");

        var failed = await Verify(user.Id, purchase);

        Assert.Equal(PlayPurchaseError.VerificationUnavailable, failed.Error);
        Assert.Null(await Stored(purchase.Token));

        google.Intercept = null;
        Assert.True((await Verify(user.Id, purchase)).Success);
    }

    [Fact]
    public async Task When_Google_is_unreachable_nothing_is_credited()
    {
        var user = await SeedUser(balance: 0m);
        var purchase = google.Buy(user.Id);
        google.Intercept = _ => throw new HttpRequestException("connection refused");

        var result = await Verify(user.Id, purchase);

        Assert.Equal(PlayPurchaseError.VerificationUnavailable, result.Error);
        Assert.Null(await Stored(purchase.Token));
    }

    private sealed class HangingTokens : IPlayAccessTokenSource
    {
        public async Task<string> GetAccessTokenAsync(CancellationToken cancellationToken)
        {
            await Task.Delay(Timeout.Infinite, cancellationToken);
            return "never";
        }
    }

    [Fact]
    public async Task A_hanging_token_server_answers_retry_later_instead_of_holding_the_request()
    {
        var user = await SeedUser(balance: 0m);
        var purchase = google.Buy(user.Id);
        var hanging = PlayBillingConnection.Enabled(FakeGooglePlay.PackageName, FakeGooglePlay.Catalog, new HangingTokens());
        await using var request = NewRequest(hanging);
        var service = request.Service;
        service.VerifyTimeout = TimeSpan.FromMilliseconds(200);

        var result = await service.VerifyPurchaseAsync(user.Id, purchase.ProductId, purchase.Token, null);

        Assert.Equal(PlayPurchaseError.VerificationUnavailable, result.Error);
        Assert.Null(await Stored(purchase.Token));
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized, "authError")]
    [InlineData(HttpStatusCode.Forbidden, "permissionDenied")]
    [InlineData(HttpStatusCode.NotFound, "applicationNotFound")]
    public async Task When_Google_refuses_our_setup_billing_is_unavailable(HttpStatusCode status, string reason)
    {
        var user = await SeedUser(balance: 0m);
        var purchase = google.Buy(user.Id);
        google.Intercept = _ => FakeGooglePlay.Error(status, reason);

        var result = await Verify(user.Id, purchase);

        Assert.Equal(PlayPurchaseError.BillingUnavailable, result.Error);
        Assert.Null(await Stored(purchase.Token));
    }

    [Fact]
    public async Task Without_configuration_purchases_are_refused_and_no_pack_is_offered()
    {
        var user = await SeedUser(balance: 0m);
        var purchase = google.Buy(user.Id);
        var disabled = PlayBillingConnection.Disabled("PlayBilling:ServiceAccountJson is not set");
        await using var request = NewRequest(disabled);

        var result = await request.Service.VerifyPurchaseAsync(user.Id, purchase.ProductId, purchase.Token, null);
        var catalog = request.Service.GetProducts(user.Id);

        Assert.Equal(PlayPurchaseError.BillingUnavailable, result.Error);
        Assert.Equal("الشراء عبر Google Play غير متاح حاليًا. حاول لاحقًا.", result.Message);
        Assert.False(catalog.Enabled);
        Assert.Empty(catalog.Products);
        Assert.Equal(result.Message, catalog.Message);
        Assert.Empty(google.Requests);
    }

    [Fact]
    public async Task The_catalog_lists_the_packs_by_points_with_the_callers_account_id()
    {
        var user = await SeedUser(balance: 0m);
        var connection = PlayBillingConnection.Enabled(FakeGooglePlay.PackageName,
            [new PlayProduct("points_5000", 5000), new PlayProduct("points_500", 500), new PlayProduct("points_1000", 1000)], new FakeTokens());
        await using var request = NewRequest(connection);

        var catalog = request.Service.GetProducts(user.Id);

        Assert.True(catalog.Enabled);
        Assert.Null(catalog.Message);
        Assert.Equal(PlayAccountId.For(user.Id), catalog.ObfuscatedAccountId);
        Assert.Equal([("points_500", 500), ("points_1000", 1000), ("points_5000", 5000)], catalog.Products.Select(p => (p.ProductId, p.Points)));
    }

    // ===== Consuming =====

    [Fact]
    public async Task A_failed_consume_keeps_the_purchase_credited_and_the_worker_consumes_it_later()
    {
        var user = await SeedUser(balance: 0m);
        var purchase = google.Buy(user.Id);
        google.Intercept = request => request.RequestUri!.AbsolutePath.EndsWith(":consume")
            ? FakeGooglePlay.Error(HttpStatusCode.ServiceUnavailable, "backendError")
            : null;

        var result = await Verify(user.Id, purchase);

        Assert.Equal((true, 1000), (result.Success, result.PointsAdded));
        var owed = await Stored(purchase.Token);
        Assert.Null(owed!.ConsumedAt);
        Assert.Equal(1, owed.ConsumeAttempts);
        Assert.Contains("503", owed.LastConsumeError);
        Assert.Equal(clock.UtcNow + TimeSpan.FromMinutes(5), owed.NextConsumeAttemptAt);

        // Not due yet: the worker waits for the backoff.
        google.Intercept = null;
        Assert.Equal(0, await RetryConsumes());
        Assert.Equal(0, google.Purchases[purchase.Token].ConsumptionState);

        clock.Advance(TimeSpan.FromMinutes(5));
        Assert.Equal(1, await RetryConsumes());

        var consumed = await Stored(purchase.Token);
        Assert.NotNull(consumed!.ConsumedAt);
        Assert.Null(consumed.NextConsumeAttemptAt);
        Assert.Null(consumed.LastConsumeError);
        Assert.Equal(1, google.Purchases[purchase.Token].ConsumptionState);
        Assert.Equal(1000m, await Balance(user.Id));
    }

    [Fact]
    public async Task A_purchase_credited_just_before_a_crash_is_consumed_by_the_worker()
    {
        // The request credited it and then the process died before consuming (simulated by an error nothing expects,
        // so no attempt is recorded): the row still says a consume is owed.
        var user = await SeedUser(balance: 0m);
        var purchase = google.Buy(user.Id);
        google.Intercept = request => request.RequestUri!.AbsolutePath.EndsWith(":consume") ? throw new InvalidOperationException("process died") : null;
        Assert.True((await Verify(user.Id, purchase)).Success);
        google.Intercept = null;
        Assert.Equal(0, await RetryConsumes()); // within the grace period the request itself is still expected to finish

        clock.Advance(PlayBillingService.ConsumeGrace);
        Assert.Equal(1, await RetryConsumes());

        Assert.NotNull((await Stored(purchase.Token))!.ConsumedAt);
    }

    [Fact]
    public async Task Consume_retries_back_off_to_hourly()
    {
        Assert.Equal([5, 10, 20, 40, 60, 60, 60],
            Enumerable.Range(1, 7).Select(attempt => PlayBillingService.ConsumeBackoff(attempt).TotalMinutes));

        var user = await SeedUser(balance: 0m);
        var purchase = google.Buy(user.Id);
        google.Intercept = request => request.RequestUri!.AbsolutePath.EndsWith(":consume")
            ? FakeGooglePlay.Error(HttpStatusCode.InternalServerError, "backendError")
            : null;
        Assert.True((await Verify(user.Id, purchase)).Success);

        foreach (var wait in new[] { 5, 10, 20 })
        {
            clock.Advance(TimeSpan.FromMinutes(wait) - TimeSpan.FromSeconds(1));
            await RetryConsumes();
            clock.Advance(TimeSpan.FromSeconds(1));
            await RetryConsumes();
        }

        var owed = await Stored(purchase.Token);
        Assert.Equal(4, owed!.ConsumeAttempts);
        Assert.Equal(clock.UtcNow + TimeSpan.FromMinutes(40), owed.NextConsumeAttemptAt);
        Assert.Equal(4, google.Count("POST", purchase.Token + ":consume"));
    }

    [Fact]
    public async Task A_consume_whose_answer_got_lost_counts_as_done()
    {
        var user = await SeedUser(balance: 0m);
        var purchase = google.Buy(user.Id);
        google.Intercept = request =>
        {
            if (!request.RequestUri!.AbsolutePath.EndsWith(":consume")) return null;
            google.Purchases[purchase.Token].ConsumptionState = 1; // Google consumed it...
            return FakeGooglePlay.Error(HttpStatusCode.BadGateway, "backendError"); // ...but the answer didn't arrive
        };

        Assert.True((await Verify(user.Id, purchase)).Success);

        var stored = await Stored(purchase.Token);
        Assert.NotNull(stored!.ConsumedAt);
        Assert.Equal(0, stored.ConsumeAttempts);
    }

    [Fact]
    public async Task A_purchase_the_app_consumed_itself_is_still_credited_once()
    {
        var user = await SeedUser(balance: 0m);
        var purchase = google.Buy(user.Id, change: p => p.ConsumptionState = 1);

        var result = await Verify(user.Id, purchase);

        Assert.True(result.Success, result.Code);
        Assert.NotNull((await Stored(purchase.Token))!.ConsumedAt);
        Assert.Equal(0, google.Count("POST", ":consume"));
    }

    [Fact]
    public async Task A_purchase_refunded_before_it_could_be_consumed_loses_its_points()
    {
        var user = await SeedUser(balance: 0m);
        var purchase = google.Buy(user.Id);
        google.Intercept = request => request.RequestUri!.AbsolutePath.EndsWith(":consume")
            ? FakeGooglePlay.Error(HttpStatusCode.ServiceUnavailable, "backendError")
            : null;
        Assert.True((await Verify(user.Id, purchase)).Success);

        // Three days without a consume: Google refunded it.
        google.Intercept = null;
        google.Purchases[purchase.Token].PurchaseState = 1;
        clock.Advance(TimeSpan.FromDays(3));
        Assert.Equal(0, await RetryConsumes());

        Assert.Equal(0m, await Balance(user.Id));
        var stored = await Stored(purchase.Token);
        Assert.Equal(PlayPurchaseStatus.Voided, stored!.Status);
        Assert.Null(stored.NextConsumeAttemptAt);
        Assert.Equal([TransactionType.PlayPurchase, TransactionType.PlayRefund], (await Ledger(user.Id)).Select(t => t.Type));

        // Nothing more is owed.
        clock.Advance(TimeSpan.FromHours(2));
        var consumeCalls = google.Count("POST", purchase.Token + ":consume");
        Assert.Equal(0, await RetryConsumes());
        Assert.Equal(consumeCalls, google.Count("POST", purchase.Token + ":consume"));
    }

    [Fact]
    public async Task A_replay_retries_a_consume_that_is_due()
    {
        var user = await SeedUser(balance: 0m);
        var purchase = google.Buy(user.Id);
        google.Intercept = request => request.RequestUri!.AbsolutePath.EndsWith(":consume")
            ? FakeGooglePlay.Error(HttpStatusCode.ServiceUnavailable, "backendError")
            : null;
        Assert.True((await Verify(user.Id, purchase)).Success);
        google.Intercept = null;

        Assert.True((await Verify(user.Id, purchase)).Success); // not due yet: no consume
        Assert.Null((await Stored(purchase.Token))!.ConsumedAt);

        clock.Advance(TimeSpan.FromMinutes(5));
        var replay = await Verify(user.Id, purchase);

        Assert.Equal((true, 1000, 1000m), (replay.Success, replay.PointsAdded, replay.CurrentBalance));
        Assert.NotNull((await Stored(purchase.Token))!.ConsumedAt);
    }

    // ===== Voided purchases =====

    [Fact]
    public async Task A_voided_purchase_takes_its_points_back_even_below_zero_once()
    {
        var user = await SeedUser(balance: 0m);
        var author = await SeedUser(balance: 0m);
        var purchase = google.Buy(user.Id, "points_1000");
        Assert.True((await Verify(user.Id, purchase)).Success);
        await using (var request = NewRequest())
        {
            await WalletTesting.Wallet(request.Db, clock)
                .TransferPointsAsync(user.Id, author.Id, 800, TransactionType.GiftSent, TransactionType.GiftReceived, "s", "r");
        }

        // A chargeback after the author's hold ended (a refund within it takes the gift back: EarningsHoldTests).
        clock.Advance(TimeSpan.FromDays(31));
        google.Void(purchase.Token, recordedAt: clock.UtcNow.AddHours(-1), reason: 1, source: 0);
        Assert.Equal(1, await SyncVoided());

        Assert.Equal(-800m, await Balance(user.Id));
        Assert.Equal(800m, await Balance(author.Id)); // the author keeps the gift: it was released
        var refund = (await Ledger(user.Id)).Single(t => t.Type == TransactionType.PlayRefund);
        Assert.Equal((-1000m, 200m, -800m), (refund.Amount, refund.BalanceBefore, refund.BalanceAfter));
        Assert.Contains(purchase.OrderId!, refund.Description);

        var stored = await Stored(purchase.Token);
        Assert.Equal(PlayPurchaseStatus.Voided, stored!.Status);
        Assert.Equal(refund.RelatedRequestId, stored.Id);
        Assert.Equal((1, 0), (stored.VoidedReason, stored.VoidedSource));
        Assert.Equal(clock.UtcNow.AddHours(-1), stored.VoidedAt!.Value, TimeSpan.FromMilliseconds(1));

        // Seen again by every later poll (the windows overlap): nothing more happens.
        clock.Advance(TimeSpan.FromHours(1));
        Assert.Equal(0, await SyncVoided());
        Assert.Equal(-800m, await Balance(user.Id));
        Assert.Single(await Ledger(user.Id), t => t.Type == TransactionType.PlayRefund);
    }

    [Fact]
    public async Task A_void_reported_many_times_at_once_takes_the_points_back_once()
    {
        var user = await SeedUser(balance: 300m);
        var purchase = google.Buy(user.Id, "points_500");
        Assert.True((await Verify(user.Id, purchase)).Success);
        var voided = new PlayVoidedPurchase(purchase.Token, purchase.OrderId, DateTime.UtcNow, 7, 2);

        var results = await Parallel(8, async () =>
        {
            await using var request = NewRequest();
            return await request.Service.ApplyVoidAsync(voided);
        });

        Assert.Single(results, applied => applied);
        Assert.Equal(300m, await Balance(user.Id));
        Assert.Single(await Ledger(user.Id), t => t.Type == TransactionType.PlayRefund);
    }

    [Fact]
    public async Task A_purchase_voided_before_anyone_claimed_it_can_never_be_credited()
    {
        var user = await SeedUser(balance: 0m);
        var purchase = google.Buy(user.Id);
        google.Void(purchase.Token, recordedAt: clock.UtcNow.AddMinutes(-30));
        google.Purchases[purchase.Token].PurchaseState = 0; // even if Google still showed it as purchased

        Assert.Equal(1, await SyncVoided());
        var result = await Verify(user.Id, purchase);

        Assert.Equal(PlayPurchaseError.PurchaseVoided, result.Error);
        Assert.Equal(0m, await Balance(user.Id));
        Assert.Empty(await Ledger(user.Id));
        var marker = await Stored(purchase.Token);
        Assert.Equal((PlayPurchaseStatus.VoidedUnclaimed, (string?)null, (string?)null, 0), (marker!.Status, marker.UserId, marker.ProductId, marker.Points));
        Assert.Equal(purchase.OrderId, marker.OrderId);
    }

    [Fact]
    public async Task Sending_a_voided_purchase_again_is_refused()
    {
        var user = await SeedUser(balance: 0m);
        var purchase = google.Buy(user.Id);
        Assert.True((await Verify(user.Id, purchase)).Success);
        google.Void(purchase.Token, recordedAt: clock.UtcNow);
        await SyncVoided();

        var replay = await Verify(user.Id, purchase);

        Assert.Equal(PlayPurchaseError.PurchaseVoided, replay.Error);
        Assert.Equal(0m, await Balance(user.Id));
    }

    [Fact]
    public async Task A_void_racing_the_purchase_request_is_never_lost()
    {
        // Whichever records the token first, the end state is the same: no points from a voided purchase.
        var users = await Task.WhenAll(Enumerable.Range(0, 6).Select(_ => SeedUser(balance: 0m)));
        var purchases = users.Select(u => (User: u, Purchase: google.Buy(u.Id, "points_500"))).ToList();

        await Task.WhenAll(purchases.SelectMany(p => new Task[]
        {
            Task.Run(() => Verify(p.User.Id, p.Purchase)),
            Task.Run(async () =>
            {
                await using var request = NewRequest();
                await request.Service.ApplyVoidAsync(new PlayVoidedPurchase(p.Purchase.Token, p.Purchase.OrderId, DateTime.UtcNow, 1, 0));
            })
        }));

        foreach (var (user, purchase) in purchases)
        {
            Assert.Equal(0m, await Balance(user.Id) ?? 0m);
            var stored = await Stored(purchase.Token);
            Assert.Contains(stored!.Status, new[] { PlayPurchaseStatus.Voided, PlayPurchaseStatus.VoidedUnclaimed });
            Assert.Equal(0m, (await Ledger(user.Id)).Sum(t => t.Amount));
        }
    }

    [Fact]
    public async Task The_voided_poll_reads_from_its_cursor_follows_every_page_and_moves_on_only_when_done()
    {
        var user = await SeedUser(balance: 0m);
        google.VoidedPageSize = 2;
        var start = clock.UtcNow;
        var tokens = new List<string>();
        for (var i = 0; i < 5; i++)
        {
            var purchase = google.Buy(user.Id, "points_500");
            Assert.True((await Verify(user.Id, purchase)).Success);
            google.Void(purchase.Token, recordedAt: start.AddMinutes(-i));
            tokens.Add(purchase.Token);
        }

        // First poll: everything Google keeps (30 days, less a margin), every page.
        Assert.Equal(5, await SyncVoided());
        var lists = google.Requests.Where(r => r.Path.EndsWith("/voidedpurchases")).ToList();
        Assert.Equal(3, lists.Count);
        Assert.Contains($"startTime={GooglePlayApi.ToUnixMillis(start - PlayBillingService.VoidedLookback)}", lists[0].Query);
        Assert.DoesNotContain("token=", lists[0].Query);
        Assert.Contains("token=2", lists[1].Query);
        Assert.Contains("token=4", lists[2].Query);
        Assert.Equal(0m, await Balance(user.Id));
        await using (var db = database.CreateContext())
        {
            Assert.Equal(start, (await db.PlaySyncCursors.SingleAsync(c => c.Name == PlayBillingService.VoidedPurchasesCursor)).SyncedUntil,
                TimeSpan.FromMilliseconds(1));
        }

        // An hour later: from the cursor, less the overlap.
        clock.Advance(TimeSpan.FromHours(1));
        google.Requests.Clear();
        Assert.Equal(0, await SyncVoided());
        Assert.Contains($"startTime={GooglePlayApi.ToUnixMillis(start - PlayBillingService.VoidedOverlap)}", google.Requests.First().Query);

        // A failure on a later page leaves the cursor where it was, so the next poll reads the window again.
        var cursorBefore = clock.UtcNow;
        clock.Advance(TimeSpan.FromHours(1));
        google.Intercept = request => request.RequestUri!.Query.Contains("token=") ? FakeGooglePlay.Error(HttpStatusCode.InternalServerError, "backendError") : null;
        await Assert.ThrowsAsync<PlayApiException>(SyncVoided);
        await using (var db = database.CreateContext())
        {
            Assert.Equal(cursorBefore, (await db.PlaySyncCursors.SingleAsync(c => c.Name == PlayBillingService.VoidedPurchasesCursor)).SyncedUntil,
                TimeSpan.FromMilliseconds(1));
        }
        foreach (var token in tokens)
        {
            Assert.Equal(PlayPurchaseStatus.Voided, (await Stored(token))!.Status);
        }
    }

    [Fact]
    public async Task A_cursor_older_than_Google_keeps_is_clamped_to_thirty_days()
    {
        await using (var db = database.CreateContext())
        {
            await db.PlaySyncCursors.Where(c => c.Name == PlayBillingService.VoidedPurchasesCursor).ExecuteDeleteAsync();
            db.PlaySyncCursors.Add(new PlaySyncCursor
            {
                Name = PlayBillingService.VoidedPurchasesCursor, SyncedUntil = clock.UtcNow.AddDays(-45), UpdatedAt = clock.UtcNow.AddDays(-45)
            });
            await db.SaveChangesAsync();
        }

        await SyncVoided();

        Assert.Contains($"startTime={GooglePlayApi.ToUnixMillis(clock.UtcNow - PlayBillingService.VoidedLookback)}", google.Requests.Single().Query);
    }

    [Fact]
    public async Task A_cursor_ahead_of_the_clock_never_asks_Google_for_the_future()
    {
        await using (var db = database.CreateContext())
        {
            db.PlaySyncCursors.Add(new PlaySyncCursor
            {
                Name = PlayBillingService.VoidedPurchasesCursor, SyncedUntil = clock.UtcNow.AddDays(3), UpdatedAt = clock.UtcNow
            });
            await db.SaveChangesAsync();
        }

        await SyncVoided();

        Assert.Contains($"startTime={GooglePlayApi.ToUnixMillis(clock.UtcNow - PlayBillingService.VoidedOverlap)}", google.Requests.Single().Query);
    }

    [Fact]
    public async Task A_voided_token_longer_than_any_accepted_is_skipped_without_stalling_the_poll()
    {
        var user = await SeedUser(balance: 0m);
        var purchase = google.Buy(user.Id);
        Assert.True((await Verify(user.Id, purchase)).Success);
        var overlong = new string('x', PlayPurchase.PurchaseTokenMaxLength + 1);
        google.Void(overlong, recordedAt: clock.UtcNow.AddMinutes(-2), orderId: "GPA.overlong");
        google.Void(purchase.Token, recordedAt: clock.UtcNow.AddMinutes(-1));

        Assert.Equal(1, await SyncVoided());

        Assert.Null(await Stored(overlong));
        Assert.Equal(PlayPurchaseStatus.Voided, (await Stored(purchase.Token))!.Status);
        await using var db = database.CreateContext();
        Assert.True(await db.PlaySyncCursors.AnyAsync(c => c.Name == PlayBillingService.VoidedPurchasesCursor));
    }

    // ===== Background worker =====

    private PlayBillingWorker Worker(PlayBillingConnection connection, out ServiceProvider provider)
    {
        var services = new ServiceCollection();
        services.AddScoped(_ => database.CreateContext());
        services.AddScoped(scope =>
        {
            var db = scope.GetRequiredService<ApplicationDbContext>();
            return new PlayBillingService(connection, google.Api(connection), new PlayPurchaseRepository(db), new UserWalletRepository(db),
                new PointTransactionRepository(db), WalletTesting.Wallet(db, clock), new TransactionManager(db), clock,
                NullLogger<PlayBillingService>.Instance);
        });
        provider = services.BuildServiceProvider();
        return new PlayBillingWorker(provider.GetRequiredService<IServiceScopeFactory>(), connection, clock, NullLogger<PlayBillingWorker>.Instance);
    }

    [Fact]
    public async Task The_worker_retries_owed_consumes_every_tick_and_reads_voids_every_hour()
    {
        var worker = Worker(google.Connection(), out var provider);
        await using var _ = provider;
        var user = await SeedUser(balance: 0m);
        var purchase = google.Buy(user.Id);
        google.Intercept = request => request.RequestUri!.AbsolutePath.EndsWith(":consume")
            ? FakeGooglePlay.Error(HttpStatusCode.ServiceUnavailable, "backendError")
            : null;
        Assert.True((await Verify(user.Id, purchase)).Success);
        google.Intercept = null;
        int VoidedReads() => google.Requests.Count(r => r.Path.EndsWith("/voidedpurchases"));

        await worker.RunOnceAsync(CancellationToken.None); // consume not due yet; first voided read
        Assert.Equal(1, VoidedReads());
        Assert.Null((await Stored(purchase.Token))!.ConsumedAt);

        clock.Advance(PlayBillingWorker.Interval);
        await worker.RunOnceAsync(CancellationToken.None); // consume due; no voided read within the hour
        Assert.NotNull((await Stored(purchase.Token))!.ConsumedAt);
        Assert.Equal(1, VoidedReads());

        google.Void(purchase.Token, recordedAt: clock.UtcNow);
        clock.Advance(PlayBillingWorker.VoidedSyncInterval - PlayBillingWorker.Interval);
        await worker.RunOnceAsync(CancellationToken.None);
        Assert.Equal(2, VoidedReads());
        Assert.Equal(PlayPurchaseStatus.Voided, (await Stored(purchase.Token))!.Status);
        Assert.Equal(0m, await Balance(user.Id));
    }

    [Fact]
    public async Task A_worker_whose_voided_read_fails_keeps_consuming_and_tries_again_in_an_hour()
    {
        var worker = Worker(google.Connection(), out var provider);
        await using var _ = provider;
        google.Intercept = request => request.RequestUri!.AbsolutePath.EndsWith("/voidedpurchases")
            ? FakeGooglePlay.Error(HttpStatusCode.InternalServerError, "backendError")
            : null;

        await worker.RunOnceAsync(CancellationToken.None); // logged, not thrown
        clock.Advance(PlayBillingWorker.Interval);
        await worker.RunOnceAsync(CancellationToken.None);
        Assert.Equal(1, google.Requests.Count(r => r.Path.EndsWith("/voidedpurchases")));

        clock.Advance(PlayBillingWorker.VoidedSyncInterval);
        await worker.RunOnceAsync(CancellationToken.None);
        Assert.Equal(2, google.Requests.Count(r => r.Path.EndsWith("/voidedpurchases")));
    }

    [Fact]
    public async Task With_billing_disabled_the_worker_stops_at_once_without_calling_Google()
    {
        var worker = Worker(PlayBillingConnection.Disabled("PlayBilling:ServiceAccountJson is not set"), out var provider);
        await using var _ = provider;

        await worker.StartAsync(CancellationToken.None);
        await worker.ExecuteTask!.WaitAsync(TimeSpan.FromSeconds(10)); // no startup delay, no timer: it just ends

        Assert.True(worker.ExecuteTask.IsCompletedSuccessfully);
        Assert.Empty(google.Requests);
        await worker.StopAsync(CancellationToken.None);
    }
}
