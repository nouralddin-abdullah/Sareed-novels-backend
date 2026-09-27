using System.Security.Claims;
using Application.Services;
using Domain.Entities;
using Infrastructure.Persistence;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Infrastructure.Services;

/// <inheritdoc />
internal sealed class TokenRevocationService(ApplicationDbContext db, TokenCutoffCache cache, TimeProvider time) : ITokenRevocationService
{
    public async Task RevokeAllTokensAsync(string userId, CancellationToken cancellationToken = default)
    {
        var cutoff = User.TokenCutoff(time.GetUtcNow().UtcDateTime);

        // Only ever moves forward: a revocation never lets back in a token an earlier one refused.
        await db.Users
            .Where(u => u.Id == userId)
            .ExecuteUpdateAsync(s => s.SetProperty(
                u => u.TokensValidAfter,
                u => u.TokensValidAfter != null && u.TokensValidAfter > cutoff ? u.TokensValidAfter : cutoff), cancellationToken);

        cache.Forget(userId);

        // The phones those sessions registered for push notifications would otherwise keep receiving the account's
        // notifications. The app registers its phone again when it signs in.
        await db.UserDevices
            .Where(d => d.UserId == userId)
            .ExecuteDeleteAsync(cancellationToken);
    }

    public async Task<bool> IsTokenActiveAsync(string userId, DateTime issuedAtUtc, CancellationToken cancellationToken = default)
    {
        if (!cache.TryGet(userId, out var state))
        {
            var readStartedAt = cache.Generation;
            state = await db.Users
                .AsNoTracking()
                .Where(u => u.Id == userId)
                .Select(u => new UserTokenCutoff(true, u.TokensValidAfter))
                .FirstOrDefaultAsync(cancellationToken)
                ?? UserTokenCutoff.NoSuchUser;
            cache.Set(userId, state, readStartedAt);
        }

        return state.UserExists && (state.ValidAfter is not { } validAfter || issuedAtUtc >= validAfter);
    }
}

/// <summary>A user's token cut-off as last read from the database.</summary>
internal sealed record UserTokenCutoff(bool UserExists, DateTime? ValidAfter)
{
    public static readonly UserTokenCutoff NoSuchUser = new(false, null);
}

/// <summary>
/// Every user's token cut-off, kept for <see cref="Ttl"/> on this instance: the check runs on every authenticated
/// request and would otherwise be a database query each time. A revocation made on this instance applies at once;
/// one made elsewhere (another instance, a manual database update) within <see cref="Ttl"/>.
/// </summary>
internal sealed class TokenCutoffCache(IMemoryCache cache)
{
    public static readonly TimeSpan Ttl = TimeSpan.FromMinutes(1);

    private readonly object sync = new();

    // Bumped by every revocation on this instance. A read only goes into the cache if no revocation happened while it
    // ran: otherwise a read that started just before a revocation could put the old cut-off back after the
    // revocation removed it, and a revoked token would keep working until the entry expired.
    private long generation;

    public long Generation
    {
        get { lock (sync) return generation; }
    }

    public bool TryGet(string userId, out UserTokenCutoff state)
    {
        if (cache.TryGetValue(Key(userId), out UserTokenCutoff? cached) && cached is not null)
        {
            state = cached;
            return true;
        }
        state = UserTokenCutoff.NoSuchUser;
        return false;
    }

    public void Set(string userId, UserTokenCutoff state, long readStartedAt)
    {
        lock (sync)
        {
            if (generation != readStartedAt)
            {
                return;
            }
            // The app's memory cache has a size limit, which requires every entry to state its size.
            cache.Set(Key(userId), state, new MemoryCacheEntryOptions { Size = 1, AbsoluteExpirationRelativeToNow = Ttl });
        }
    }

    public void Forget(string userId)
    {
        lock (sync)
        {
            generation++;
            cache.Remove(Key(userId));
        }
    }

    private static string Key(string userId) => "token-cutoff:" + userId;
}

/// <summary>Issue times of access tokens, and the JWT bearer hook that refuses revoked ones.</summary>
public static class AccessTokens
{
    /// <summary>
    /// Tokens issued before the "iat" claim was added carry only their expiry, and all of them lived exactly this
    /// long, so they were issued at exp minus this.
    /// </summary>
    public static readonly TimeSpan LegacyLifetime = TimeSpan.FromDays(60);

    /// <summary>When the token was issued (UTC): its "iat", else derived from its "exp"; null if it has neither.</summary>
    public static DateTime? IssuedAt(SecurityToken token)
    {
        var issuedAt = token switch
        {
            JsonWebToken jwt => jwt.IssuedAt,
            System.IdentityModel.Tokens.Jwt.JwtSecurityToken jwt => jwt.IssuedAt,
            _ => DateTime.MinValue,
        };
        if (issuedAt != DateTime.MinValue)
        {
            return DateTime.SpecifyKind(issuedAt, DateTimeKind.Utc);
        }

        return token.ValidTo == DateTime.MinValue
            ? null
            : DateTime.SpecifyKind(token.ValidTo, DateTimeKind.Utc) - LegacyLifetime;
    }

    /// <summary>
    /// JwtBearerEvents.OnTokenValidated: refuses a token issued before its user's cut-off, or whose user no longer
    /// exists (<see cref="ITokenRevocationService"/>). The request then carries on unauthenticated, so [Authorize]
    /// endpoints answer 401.
    /// </summary>
    public static async Task RejectRevokedAsync(TokenValidatedContext context)
    {
        var userId = context.Principal?.FindFirstValue(ClaimTypes.NameIdentifier);
        var issuedAt = IssuedAt(context.SecurityToken);
        if (userId is null || issuedAt is null)
        {
            context.Fail("The access token has no user or issue time.");
            return;
        }

        var revocation = context.HttpContext.RequestServices.GetRequiredService<ITokenRevocationService>();
        if (!await revocation.IsTokenActiveAsync(userId, issuedAt.Value, context.HttpContext.RequestAborted))
        {
            context.Fail("This session has been signed out.");
        }
    }
}
