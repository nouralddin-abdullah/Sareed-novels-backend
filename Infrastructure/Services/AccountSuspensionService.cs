using Application.Services;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Services;

/// <inheritdoc />
internal sealed class AccountSuspensionService(
    ApplicationDbContext db,
    ITokenRevocationService tokenRevocation,
    TokenCutoffCache tokenCutoffs) : IAccountSuspensionService
{
    public async Task<DateTime?> SuspendAsync(string userId, DateTime until, CancellationToken cancellationToken = default)
    {
        // One UPDATE, and never shorter than a suspension already running.
        var updated = await db.Users
            .Where(u => u.Id == userId)
            .ExecuteUpdateAsync(s => s.SetProperty(
                u => u.SuspendedUntil,
                u => u.SuspendedUntil != null && u.SuspendedUntil > until ? u.SuspendedUntil : until), cancellationToken);
        if (updated == 0)
        {
            return null;
        }

        // Ends every session (and forgets the cached state, so the suspension applies on this instance at once).
        await tokenRevocation.RevokeAllTokensAsync(userId, cancellationToken);

        return await db.Users
            .AsNoTracking()
            .Where(u => u.Id == userId)
            .Select(u => u.SuspendedUntil)
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<bool> LiftAsync(string userId, CancellationToken cancellationToken = default)
    {
        var updated = await db.Users
            .Where(u => u.Id == userId)
            .ExecuteUpdateAsync(s => s.SetProperty(u => u.SuspendedUntil, (DateTime?)null), cancellationToken);

        // New sign-ins work on this instance at once, rather than when the cached suspension expires.
        tokenCutoffs.Forget(userId);
        return updated > 0;
    }
}
