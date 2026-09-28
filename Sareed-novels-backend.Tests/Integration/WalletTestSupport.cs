using Infrastructure.Configuration;
using Infrastructure.Persistence;
using Infrastructure.Repositories;
using Infrastructure.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Sareed_novels_backend.Tests.Integration;

/// <summary>The wallet service on a test's DbContext.</summary>
internal static class WalletTesting
{
    /// <summary>A WalletService as the API builds it, on <paramref name="db"/>, with this clock and hold.</summary>
    public static WalletService Wallet(ApplicationDbContext db, TimeProvider? clock = null,
        int holdDays = WalletSettings.DefaultEarningsHoldDays) =>
        new(NullLogger<WalletService>.Instance, new UserWalletRepository(db), new PointTransactionRepository(db),
            null!, new TransactionManager(db), clock ?? TimeProvider.System,
            Options.Create(new WalletSettings { EarningsHoldDays = holdDays }));
}
