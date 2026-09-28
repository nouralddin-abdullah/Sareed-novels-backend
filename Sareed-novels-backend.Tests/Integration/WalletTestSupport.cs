using Domain.Constants;
using Domain.Entities;
using Infrastructure.Configuration;
using Infrastructure.Persistence;
using Infrastructure.Repositories;
using Infrastructure.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Sareed_novels_backend.Tests.Integration;

/// <summary>The wallet service on a test's DbContext, and ledger rows for what a test starts from.</summary>
internal static class WalletTesting
{
    /// <summary>A WalletService as the API builds it, on <paramref name="db"/>, with this clock and hold.</summary>
    public static WalletService Wallet(ApplicationDbContext db, TimeProvider? clock = null,
        int holdDays = WalletSettings.DefaultEarningsHoldDays) =>
        new(NullLogger<WalletService>.Instance, new UserWalletRepository(db), new PointTransactionRepository(db),
            new WithdrawalRequestRepository(db), null!, new TransactionManager(db), clock ?? TimeProvider.System,
            Options.Create(new WalletSettings { EarningsHoldDays = holdDays }));

    /// <summary>
    /// A gift the user received long enough ago that it is withdrawable now: the ledger row only (seed the balance
    /// separately), as if earned before this test.
    /// </summary>
    public static PointTransaction ReleasedEarning(string userId, decimal amount, DateTime? releasedAt = null)
    {
        var availableAt = releasedAt ?? DateTime.UtcNow.AddDays(-1);
        return new PointTransaction
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            Type = TransactionType.GiftReceived,
            Amount = amount,
            BalanceBefore = 0,
            BalanceAfter = amount,
            Description = "هدية",
            RelatedRequestId = Guid.NewGuid(),
            AvailableAt = availableAt,
            CreatedAt = availableAt.AddDays(-WalletSettings.DefaultEarningsHoldDays)
        };
    }
}
