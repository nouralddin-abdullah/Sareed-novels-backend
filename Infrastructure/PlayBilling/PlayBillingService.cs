using Application.Services;
using Application.Wallet;
using Application.Wallet.DTOs;
using Domain.Constants;
using Domain.Entities;
using Domain.Repositories;
using Microsoft.Extensions.Logging;

namespace Infrastructure.PlayBilling;

/// <summary>
/// Point packs bought in the Android app through Google Play Billing.
/// <list type="bullet">
/// <item>Verify: the app sends the purchase token; the server asks Google (purchases.products.get) and checks the
/// state, product, account and quantity itself. The client's word is never taken for anything.</item>
/// <item>Credit: the PlayPurchases row (unique per token), the wallet credit (one SQL UPDATE) and the ledger row commit
/// together, so a token is credited once however often and concurrently it is sent; replays get the same answer.</item>
/// <item>Consume: right after crediting, on the server. If that fails the purchase stays credited and
/// <see cref="RetryConsumptionsAsync"/> (background worker) tries again with backoff, because Google refunds a purchase
/// nobody consumed or acknowledged within three days. The app must never consume or acknowledge.</item>
/// <item>Voids: <see cref="SyncVoidedPurchasesAsync"/> polls the Voided Purchases API and <see cref="ApplyVoidAsync"/>
/// takes the points back, even below zero, once per token; what the buyer's balance can't cover is taken back from the
/// authors' earnings those points paid for, while still on hold (#22).</item>
/// </list>
/// </summary>
public class PlayBillingService(
    PlayBillingConnection connection,
    GooglePlayApi api,
    IPlayPurchaseRepository purchases,
    IUserWalletRepository wallets,
    IPointTransactionRepository ledger,
    IWalletService wallet,
    ITransactionManager transactions,
    TimeProvider clock,
    ILogger<PlayBillingService> logger) : IPlayBillingService
{
    public const string VoidedPurchasesCursor = "VoidedPurchases";

    /// <summary>Google lists voids of the last 30 days only and refuses an older start; the margin covers clock skew.</summary>
    internal static readonly TimeSpan VoidedLookback = TimeSpan.FromDays(30) - TimeSpan.FromMinutes(10);

    /// <summary>
    /// Each poll starts this long before the cursor: the list is by when Google recorded the void, which can lag, and
    /// applying a void twice changes nothing.
    /// </summary>
    internal static readonly TimeSpan VoidedOverlap = TimeSpan.FromDays(1);

    internal const int MaxVoidedPages = 100;

    /// <summary>How long a purchase request waits for Google's verification (token included) before answering "retry later".</summary>
    internal TimeSpan VerifyTimeout { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>The consume right after crediting gets this head start before the worker tries too.</summary>
    internal static readonly TimeSpan ConsumeGrace = TimeSpan.FromMinutes(2);

    /// <summary>How long a purchase request waits for the consume after crediting; the worker finishes it otherwise.</summary>
    internal static readonly TimeSpan InlineConsumeTimeout = TimeSpan.FromSeconds(10);

    /// <summary>Google refunds an unconsumed, unacknowledged purchase after three days: from two, every failure is an error.</summary>
    internal static readonly TimeSpan ConsumeAlarmAge = TimeSpan.FromDays(2);

    internal const int ConsumeBatchSize = 50;

    private enum ConsumeOutcome { Consumed, NotYet, Voided }

    public PlayProductsDto GetProducts(string userId) => new()
    {
        Enabled = connection.IsEnabled,
        Message = connection.IsEnabled ? null : PlayPurchaseMessages.Unavailable,
        ObfuscatedAccountId = PlayAccountId.For(userId),
        Products = connection.IsEnabled
            ? connection.Products.Select(p => new PlayProductDto { ProductId = p.ProductId, Points = p.Points }).ToList()
            : new List<PlayProductDto>()
    };

    public async Task<PlayPurchaseResult> VerifyPurchaseAsync(string userId, string? productId, string? purchaseToken, string? orderId,
        CancellationToken cancellationToken = default)
    {
        if (!connection.IsEnabled)
        {
            logger.LogError("Google Play purchase by user {UserId} refused: billing is disabled ({Reason})", userId, connection.DisabledReason);
            return PlayPurchaseResult.Failed(PlayPurchaseError.BillingUnavailable);
        }

        productId = productId?.Trim();
        purchaseToken = purchaseToken?.Trim();
        if (string.IsNullOrEmpty(productId) || string.IsNullOrEmpty(purchaseToken) || purchaseToken.Length > PlayPurchase.PurchaseTokenMaxLength)
        {
            return PlayPurchaseResult.Failed(PlayPurchaseError.InvalidRequest);
        }

        if (connection.PointsFor(productId) is not { } points)
        {
            logger.LogWarning("Google Play purchase by user {UserId} refused: {ProductId} is not in the catalog", userId, productId);
            return PlayPurchaseResult.Failed(PlayPurchaseError.UnknownProduct);
        }

        // Seen before: answer from the record, without asking Google again.
        var existing = await purchases.GetByTokenAsync(purchaseToken);
        if (existing != null)
        {
            return await ReplayAsync(existing, userId, productId);
        }

        PlayProductPurchase? purchase;
        try
        {
            // Bounds the token request too, which the HttpClient timeout doesn't cover.
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(VerifyTimeout);
            purchase = await api.GetProductPurchaseAsync(productId, purchaseToken, timeout.Token);
        }
        catch (PlayApiException ex) when (ex.IsConfigurationError)
        {
            logger.LogError(ex, "Google Play purchase by user {UserId} not verified: Google refused our credentials or package name", userId);
            return PlayPurchaseResult.Failed(PlayPurchaseError.BillingUnavailable);
        }
        catch (PlayApiException ex)
        {
            logger.LogWarning(ex, "Google Play purchase by user {UserId} not verified: Google Play unavailable", userId);
            return PlayPurchaseResult.Failed(PlayPurchaseError.VerificationUnavailable);
        }
        catch (OperationCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning(ex, "Google Play purchase by user {UserId} not verified: Google Play took longer than {Timeout}", userId, VerifyTimeout);
            return PlayPurchaseResult.Failed(PlayPurchaseError.VerificationUnavailable);
        }

        if (purchase is null)
        {
            logger.LogWarning("Google Play purchase by user {UserId} refused: Google doesn't know the token for {ProductId}", userId, productId);
            return PlayPurchaseResult.Failed(PlayPurchaseError.PurchaseNotFound);
        }

        if (Refusal(purchase, productId, userId) is { } refusal)
        {
            logger.LogWarning(
                "Google Play purchase by user {UserId} refused: {Refusal} (order {OrderId}, product {ProductId}, state {State}, type {Type}, quantity {Quantity})",
                userId, refusal, purchase.OrderId, productId, purchase.PurchaseState, purchase.PurchaseType, purchase.Quantity);
            return PlayPurchaseResult.Failed(refusal);
        }

        if (!string.IsNullOrEmpty(orderId) && purchase.OrderId != null && orderId != purchase.OrderId)
        {
            logger.LogWarning("The app sent order {ClaimedOrderId} for Google Play order {OrderId}; recording Google's", orderId, purchase.OrderId);
        }

        var now = Now();
        var record = new PlayPurchase
        {
            Id = Guid.NewGuid(),
            PurchaseToken = purchaseToken,
            UserId = userId,
            ProductId = productId,
            OrderId = Truncate(purchase.OrderId, PlayPurchase.OrderIdMaxLength),
            Points = points,
            Status = PlayPurchaseStatus.Credited,
            IsTestPurchase = purchase.IsTestPurchase,
            PurchasedAt = purchase.PurchasedAt,
            CreatedAt = now,
            // Consumed already means the app consumed it itself (it mustn't); then there's nothing left to do.
            ConsumedAt = purchase.IsConsumed ? now : null,
            NextConsumeAttemptAt = purchase.IsConsumed ? null : now + ConsumeGrace
        };

        await wallets.EnsureExistsAsync(userId);
        var balance = await transactions.InTransactionAsync(async () =>
        {
            // The unique token index decides between concurrent requests: the loser records and credits nothing.
            if (!await purchases.TryAddAsync(record))
            {
                return (decimal?)null;
            }

            var after = await wallets.CreditAsync(userId, points);
            await ledger.CreateAsync(new PointTransaction
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                Type = TransactionType.PlayPurchase,
                Amount = points,
                BalanceBefore = after - points,
                BalanceAfter = after,
                Description = $"شراء {points} نقطة عبر Google Play{OrderSuffix(record.OrderId)}",
                RelatedRequestId = record.Id,
                // Stamped once the wallet is changed (and locked), like every other row: the ledger reads back in the
                // order the balance changed (#27).
                CreatedAt = Now()
            });
            return after;
        }, CancellationToken.None);

        if (balance is null)
        {
            var winner = await purchases.GetByTokenAsync(purchaseToken);
            if (winner is null)
            {
                logger.LogError("Google Play purchase {OrderId}: the token was recorded concurrently but its row is gone", purchase.OrderId);
                return PlayPurchaseResult.Failed(PlayPurchaseError.VerificationUnavailable);
            }
            return await ReplayAsync(winner, userId, productId);
        }

        logger.LogInformation(
            "Credited {Points} points to user {UserId} for Google Play order {OrderId} ({ProductId}, purchase {PurchaseId}{Test}); balance {Balance}",
            points, userId, record.OrderId, productId, record.Id, record.IsTestPurchase ? ", test purchase" : "", balance);
        if (purchase.IsConsumed)
        {
            logger.LogWarning("Google Play order {OrderId} was already consumed when it reached the server: the app must not consume purchases",
                record.OrderId);
        }
        else
        {
            await ConsumeNowAsync(record);
        }

        return PlayPurchaseResult.Credited(points, balance.Value);
    }

    /// <summary>A purchase Sard already recorded: the same answer for its owner, a refusal for anyone else.</summary>
    private async Task<PlayPurchaseResult> ReplayAsync(PlayPurchase existing, string userId, string productId)
    {
        if (existing.Status == PlayPurchaseStatus.VoidedUnclaimed)
        {
            logger.LogWarning("User {UserId} sent Google Play order {OrderId}, which Google voided before it was claimed", userId, existing.OrderId);
            return PlayPurchaseResult.Failed(PlayPurchaseError.PurchaseVoided);
        }

        if (existing.UserId != userId)
        {
            logger.LogWarning("User {UserId} sent Google Play order {OrderId}, already credited to user {OwnerId}",
                userId, existing.OrderId, existing.UserId);
            return PlayPurchaseResult.Failed(PlayPurchaseError.AlreadyUsedByAnotherUser);
        }

        if (existing.ProductId != productId)
        {
            return PlayPurchaseResult.Failed(PlayPurchaseError.ProductMismatch);
        }

        if (existing.Status != PlayPurchaseStatus.Credited)
        {
            return PlayPurchaseResult.Failed(PlayPurchaseError.PurchaseVoided);
        }

        // The app sending it again is a good moment for a consume that is due (it keeps to the worker's backoff).
        if (existing.ConsumedAt is null && existing.NextConsumeAttemptAt is { } due && due <= Now()
            && await ConsumeNowAsync(existing) == ConsumeOutcome.Voided)
        {
            return PlayPurchaseResult.Failed(PlayPurchaseError.PurchaseVoided);
        }

        var wallet = await wallets.GetByUserIdAsync(userId);
        return PlayPurchaseResult.Credited(existing.Points, wallet?.CurrentBalance ?? 0);
    }

    private PlayPurchaseError? Refusal(PlayProductPurchase purchase, string productId, string userId)
    {
        if (!string.IsNullOrEmpty(purchase.ProductId) && purchase.ProductId != productId)
        {
            return PlayPurchaseError.ProductMismatch;
        }

        switch (purchase.PurchaseState)
        {
            case PlayProductPurchase.Purchased:
                break;
            case PlayProductPurchase.Pending:
                return PlayPurchaseError.PurchasePending;
            default:
                // 1 is canceled; a missing or unknown state is not a completed purchase either.
                return PlayPurchaseError.PurchaseCanceled;
        }

        if (!PlayAccountId.Matches(purchase.ObfuscatedExternalAccountId, userId))
        {
            return PlayPurchaseError.AccountMismatch;
        }

        if (purchase.IsTestPurchase && !connection.AllowTestPurchases)
        {
            return PlayPurchaseError.TestPurchaseNotAllowed;
        }

        // Multi-quantity purchases are off for Sard's products; refusing leaves the purchase unconsumed, so Google refunds it.
        return purchase.QuantityOrOne == 1 ? null : PlayPurchaseError.UnsupportedQuantity;
    }

    /// <summary>
    /// Consumes purchases whose consume failed or never ran (crash, recycle), oldest due first. Called by the background
    /// worker every few minutes.
    /// </summary>
    public async Task<int> RetryConsumptionsAsync(CancellationToken cancellationToken)
    {
        if (!connection.IsEnabled)
        {
            return 0;
        }

        var consumed = 0;
        foreach (var purchase in await purchases.GetConsumptionsDueAsync(Now(), ConsumeBatchSize))
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                if (await ConsumeAsync(purchase, cancellationToken) == ConsumeOutcome.Consumed)
                {
                    consumed++;
                }
            }
            catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
            {
                logger.LogError(ex, "Retrying the consume of Google Play purchase {PurchaseId} (order {OrderId}) failed", purchase.Id, purchase.OrderId);
            }
        }
        return consumed;
    }

    /// <summary>The consume right after crediting: never fails the purchase, the worker takes over if it doesn't work.</summary>
    private async Task<ConsumeOutcome> ConsumeNowAsync(PlayPurchase purchase)
    {
        try
        {
            using var timeout = new CancellationTokenSource(InlineConsumeTimeout);
            return await ConsumeAsync(purchase, timeout.Token);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Consuming Google Play purchase {PurchaseId} (order {OrderId}) failed; the background worker retries it",
                purchase.Id, purchase.OrderId);
            return ConsumeOutcome.NotYet;
        }
    }

    /// <summary>
    /// Consumes a credited purchase on Google Play. If that fails, asks Google what state the purchase is in: consumed
    /// (an earlier answer got lost) counts as done, canceled means Google took the money back so the points go too, and
    /// anything else is recorded for a retry with backoff (5 minutes, doubling, at most hourly).
    /// </summary>
    private async Task<ConsumeOutcome> ConsumeAsync(PlayPurchase purchase, CancellationToken cancellationToken)
    {
        var productId = purchase.ProductId!;
        try
        {
            await api.ConsumeAsync(productId, purchase.PurchaseToken, cancellationToken);
            await purchases.MarkConsumedAsync(purchase.Id, Now());
            logger.LogInformation("Consumed Google Play purchase {PurchaseId} (order {OrderId})", purchase.Id, purchase.OrderId);
            return ConsumeOutcome.Consumed;
        }
        catch (PlayApiException consumeError)
        {
            PlayProductPurchase? state = null;
            try
            {
                state = await api.GetProductPurchaseAsync(productId, purchase.PurchaseToken, cancellationToken);
            }
            catch (PlayApiException)
            {
                // Keep the consume error.
            }

            if (state is { IsConsumed: true })
            {
                await purchases.MarkConsumedAsync(purchase.Id, Now());
                logger.LogInformation("Google Play purchase {PurchaseId} (order {OrderId}) was already consumed", purchase.Id, purchase.OrderId);
                return ConsumeOutcome.Consumed;
            }

            if (state is { PurchaseState: PlayProductPurchase.Canceled })
            {
                logger.LogWarning("Google Play purchase {PurchaseId} (order {OrderId}) was canceled before it was consumed; taking its points back",
                    purchase.Id, purchase.OrderId);
                await ApplyVoidAsync(new PlayVoidedPurchase(purchase.PurchaseToken, purchase.OrderId, null, null, null), cancellationToken);
                return ConsumeOutcome.Voided;
            }

            var now = Now();
            var attempts = purchase.ConsumeAttempts + 1;
            var next = now + ConsumeBackoff(attempts);
            await purchases.RecordConsumeFailureAsync(purchase.Id, consumeError.Message, next);
            // Google's three days run from the purchase, which the app may have sent late.
            var purchasedAt = purchase.PurchasedAt ?? purchase.CreatedAt;
            if (now - purchasedAt >= ConsumeAlarmAge)
            {
                logger.LogError(consumeError,
                    "Google Play purchase {PurchaseId} (order {OrderId}, bought {PurchasedAt}) is still not consumed after {Attempts} attempts: " +
                    "Google refunds it three days after the purchase. Next attempt {Next}",
                    purchase.Id, purchase.OrderId, purchasedAt, attempts, next);
            }
            else
            {
                logger.LogWarning(consumeError, "Consuming Google Play purchase {PurchaseId} (order {OrderId}) failed (attempt {Attempts}); next attempt {Next}",
                    purchase.Id, purchase.OrderId, attempts, next);
            }
            return ConsumeOutcome.NotYet;
        }
    }

    internal static TimeSpan ConsumeBackoff(int attempts) =>
        TimeSpan.FromMinutes(Math.Min(60, 5 * Math.Pow(2, Math.Clamp(attempts - 1, 0, 10))));

    /// <summary>
    /// Reads the voids Google recorded since the persisted cursor (minus an overlap; at most 30 days back) and applies
    /// each. The cursor only moves once every page is applied, so a failure means the next poll re-reads the window.
    /// Called by the background worker every hour. Returns how many voids changed something.
    /// </summary>
    public async Task<int> SyncVoidedPurchasesAsync(CancellationToken cancellationToken)
    {
        if (!connection.IsEnabled)
        {
            return 0;
        }

        var now = Now();
        var oldestAllowed = now - VoidedLookback;
        var cursor = await purchases.GetSyncCursorAsync(VoidedPurchasesCursor);
        // A cursor ahead of this clock (a host with a clock that ran fast) must not ask Google for the future.
        var start = cursor is { } syncedUntil ? Min(syncedUntil, now) - VoidedOverlap : oldestAllowed;
        if (start < oldestAllowed)
        {
            if (cursor is not null)
            {
                logger.LogError(
                    "Voided Google Play purchases were last read up to {Cursor}, but Google keeps only 30 days: voids recorded before {Oldest} can't be read, check Play Console's order management for them",
                    cursor, oldestAllowed);
            }
            start = oldestAllowed;
        }

        var applied = 0;
        var seen = 0;
        string? pageToken = null;
        for (var page = 0; ; page++)
        {
            if (page == MaxVoidedPages)
            {
                logger.LogError("Stopped reading voided Google Play purchases after {Pages} pages; the next poll starts over from {Start}", page, start);
                return applied;
            }

            var result = await api.ListVoidedPurchasesAsync(start, pageToken, cancellationToken);
            foreach (var voided in result.Purchases)
            {
                seen++;
                if (await ApplyVoidAsync(voided, cancellationToken))
                {
                    applied++;
                }
            }

            pageToken = result.NextPageToken;
            if (pageToken is null)
            {
                break;
            }
        }

        await purchases.AdvanceSyncCursorAsync(VoidedPurchasesCursor, now, now);
        logger.LogInformation("Read {Seen} voided Google Play purchases recorded since {Start}; {Applied} were new", seen, start, applied);
        return applied;
    }

    public async Task<bool> ApplyVoidAsync(PlayVoidedPurchase voided, CancellationToken cancellationToken = default)
    {
        if (voided.PurchaseToken.Length > PlayPurchase.PurchaseTokenMaxLength)
        {
            // Verification refuses such tokens, so it was never credited; recording it would fail and stall the poll.
            logger.LogWarning("Skipped voided Google Play order {OrderId}: its token is longer than Sard accepts", voided.OrderId);
            return false;
        }

        var purchase = await purchases.GetByTokenAsync(voided.PurchaseToken);
        if (purchase is null)
        {
            // Nobody claimed it yet: record it, so it can't be credited later (Google may still show it as purchased).
            var marker = new PlayPurchase
            {
                Id = Guid.NewGuid(),
                PurchaseToken = voided.PurchaseToken,
                OrderId = Truncate(voided.OrderId, PlayPurchase.OrderIdMaxLength),
                Status = PlayPurchaseStatus.VoidedUnclaimed,
                CreatedAt = Now(),
                VoidedAt = voided.VoidedAt,
                VoidedReason = voided.VoidedReason,
                VoidedSource = voided.VoidedSource
            };
            if (await purchases.TryAddAsync(marker))
            {
                logger.LogInformation("Google voided Play order {OrderId} before anyone claimed it; it can't be credited now", voided.OrderId);
                return true;
            }

            // A purchase request recorded the token in the meantime: void that.
            purchase = await purchases.GetByTokenAsync(voided.PurchaseToken);
            if (purchase is null)
            {
                return false;
            }
        }

        if (purchase.Status != PlayPurchaseStatus.Credited || purchase.UserId is not { } userId)
        {
            return false; // voided already
        }

        await wallets.EnsureExistsAsync(userId);
        var outcome = await transactions.InTransactionAsync(async () =>
        {
            // Only the request that moves the row from Credited takes the points back.
            if (!await purchases.TryMarkVoidedAsync(purchase.Id, voided.VoidedAt, voided.VoidedReason, voided.VoidedSource))
            {
                return ((decimal Balance, decimal Reversed)?)null;
            }

            // Held until the commit: the clawback below changes this wallet again, and SQL Server lets go of the row's
            // index key between two UPDATEs, where a second refund of this buyer's could slip in and deadlock with it.
            await wallets.LockBalanceAsync(userId);
            var after = await wallets.DebitAllowingNegativeAsync(userId, purchase.Points);
            await ledger.CreateAsync(new PointTransaction
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                Type = TransactionType.PlayRefund,
                Amount = -purchase.Points,
                BalanceBefore = after + purchase.Points,
                BalanceAfter = after,
                Description = $"استرجاع {purchase.Points} نقطة: أُلغي شراء عبر Google Play أو استُرد مبلغه{OrderSuffix(purchase.OrderId)}",
                RelatedRequestId = purchase.Id,
                CreatedAt = Now()
            });

            // What the balance couldn't cover was given away: take it back from the earnings those points paid for, as
            // long as they are still on hold, and give it back to the buyer (#22 rule 4). Same transaction: all or nothing.
            var reversed = await wallet.ReverseHeldEarningsAsync(userId, purchase.Id, purchase.CreatedAt,
                EarningsClawback.Deficit(purchase.Points, after));
            return (after + reversed, reversed);
        }, CancellationToken.None);

        if (outcome is not { } result)
        {
            return false;
        }

        var (balance, reversed) = result;
        logger.LogWarning(
            "Took back {Points} points from user {UserId}: Google voided Play order {OrderId} (purchase {PurchaseId}, reason {Reason}, source {Source}). {Reversed} of them were taken back from authors' earnings still on hold. Balance now {Balance}{Blocked}",
            purchase.Points, userId, purchase.OrderId, purchase.Id, voided.VoidedReason, voided.VoidedSource, reversed, balance,
            balance < 0 ? ", below zero: spending is blocked until it is topped up" : "");
        return true;
    }

    private DateTime Now() => clock.GetUtcNow().UtcDateTime;

    private static DateTime Min(DateTime a, DateTime b) => a < b ? a : b;

    private static string OrderSuffix(string? orderId) => string.IsNullOrEmpty(orderId) ? "" : $" (رقم الطلب {orderId})";

    private static string? Truncate(string? value, int maxLength) =>
        value is null || value.Length <= maxLength ? value : value[..maxLength];
}
