using System.Globalization;
using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Application.Services;
using Domain.Constants;
using Domain.Entities;
using Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Sareed_novels_backend.Tests.Integration;

/// <summary>Sign-out everywhere (<see cref="ITokenRevocationService"/>) against a real SQL Server, with a clock the tests set.</summary>
public class TokenRevocationTests(SqlServerDatabase database) : IClassFixture<SqlServerDatabase>
{
    // Half a second into a second: the cut-off keeps whole seconds, as token issue times do.
    private static readonly DateTime Noon = new(2026, 9, 27, 12, 0, 0, 500, DateTimeKind.Utc);
    private static readonly DateTime NoonSharp = new(2026, 9, 27, 12, 0, 0, DateTimeKind.Utc);

    private sealed class Clock(DateTime utcNow) : TimeProvider
    {
        public DateTime UtcNow { get; set; } = utcNow;
        public override DateTimeOffset GetUtcNow() => new(DateTime.SpecifyKind(UtcNow, DateTimeKind.Utc));
    }

    private static TokenCutoffCache NewCache() => new(new MemoryCache(new MemoryCacheOptions { SizeLimit = 100 }));

    private TokenRevocationService Service(TokenCutoffCache cache, TimeProvider clock) => new(database.CreateContext(), cache, clock);

    private async Task<User> SeedUser()
    {
        var user = Seed.User();
        await using var db = database.CreateContext();
        db.Users.Add(user);
        await db.SaveChangesAsync();
        return user;
    }

    private async Task<DateTime?> StoredCutoff(string userId)
    {
        await using var db = database.CreateContext();
        return await db.Users.Where(u => u.Id == userId).Select(u => u.TokensValidAfter).SingleAsync();
    }

    [Fact]
    public async Task Nobody_is_signed_out_until_something_revokes()
    {
        var user = await SeedUser();
        var service = Service(NewCache(), new Clock(Noon));

        Assert.Null(await StoredCutoff(user.Id));
        Assert.True(await service.IsTokenActiveAsync(user.Id, new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc)));
        Assert.True(await service.IsTokenActiveAsync(user.Id, Noon));
    }

    [Fact]
    public async Task A_revocation_refuses_tokens_issued_before_it_and_accepts_later_ones()
    {
        var user = await SeedUser();
        var service = Service(NewCache(), new Clock(Noon));

        await service.RevokeAllTokensAsync(user.Id);

        Assert.Equal(NoonSharp, await StoredCutoff(user.Id));
        Assert.False(await service.IsTokenActiveAsync(user.Id, NoonSharp.AddDays(-30)));
        Assert.False(await service.IsTokenActiveAsync(user.Id, NoonSharp.AddSeconds(-1)));
        // Issued in the revoking second (the fresh token for the session that revoked), or later.
        Assert.True(await service.IsTokenActiveAsync(user.Id, NoonSharp));
        Assert.True(await service.IsTokenActiveAsync(user.Id, NoonSharp.AddSeconds(1)));
    }

    [Fact]
    public async Task A_revocation_applies_at_once_even_when_the_old_cut_off_is_cached()
    {
        var user = await SeedUser();
        var cache = NewCache();
        var clock = new Clock(Noon);
        var issuedEarlier = NoonSharp.AddMinutes(-5);

        Assert.True(await Service(cache, clock).IsTokenActiveAsync(user.Id, issuedEarlier));
        await Service(cache, clock).RevokeAllTokensAsync(user.Id);

        Assert.False(await Service(cache, clock).IsTokenActiveAsync(user.Id, issuedEarlier));
    }

    [Fact]
    public void A_read_that_overlapped_a_revocation_is_not_cached()
    {
        var cache = NewCache();
        var readStartedAt = cache.Generation;
        cache.Forget("some-user");

        // What that read found may predate the revocation: it must not be served from the cache.
        cache.Set("some-user", new UserTokenCutoff(true, null), readStartedAt);
        Assert.False(cache.TryGet("some-user", out _));

        cache.Set("some-user", new UserTokenCutoff(true, null), cache.Generation);
        Assert.True(cache.TryGet("some-user", out var cached));
        Assert.Equal(new UserTokenCutoff(true, null), cached);
    }

    [Fact]
    public async Task A_cut_off_never_moves_back()
    {
        var user = await SeedUser();
        var clock = new Clock(Noon);
        await Service(NewCache(), clock).RevokeAllTokensAsync(user.Id);

        clock.UtcNow = Noon.AddHours(-1); // a clock set back
        await Service(NewCache(), clock).RevokeAllTokensAsync(user.Id);

        Assert.Equal(NoonSharp, await StoredCutoff(user.Id));
    }

    [Fact]
    public async Task A_revocation_unregisters_the_users_push_devices()
    {
        var (user, other) = (await SeedUser(), await SeedUser());
        await using (var db = database.CreateContext())
        {
            db.UserDevices.AddRange(Device(user.Id), Device(user.Id), Device(other.Id));
            await db.SaveChangesAsync();
        }

        await Service(NewCache(), new Clock(Noon)).RevokeAllTokensAsync(user.Id);

        // The sessions that registered them have ended: those phones must not keep getting the account's notifications.
        await using var check = database.CreateContext();
        Assert.False(await check.UserDevices.AnyAsync(d => d.UserId == user.Id));
        Assert.Equal(1, await check.UserDevices.CountAsync(d => d.UserId == other.Id));
    }

    private static UserDevice Device(string userId) => new()
    {
        Id = Guid.NewGuid(), UserId = userId, Token = "token-" + Guid.NewGuid().ToString("N"), Platform = DevicePlatforms.Android,
        CreatedAt = DateTime.UtcNow, LastSeenAt = DateTime.UtcNow
    };

    [Fact]
    public async Task Tokens_of_users_that_no_longer_exist_are_refused()
    {
        var service = Service(NewCache(), new Clock(Noon));

        Assert.False(await service.IsTokenActiveAsync(Guid.NewGuid().ToString(), Noon));
    }

    [Fact]
    public void The_issue_time_is_the_iat_claim_or_for_tokens_without_one_sixty_days_before_expiry()
    {
        var expires = new DateTime(2026, 11, 26, 12, 0, 0, DateTimeKind.Utc);
        var withIat = TokenFactory.Write("user", issuedAt: NoonSharp, expires);
        var legacy = TokenFactory.Write("user", issuedAt: null, expires);

        // JsonWebToken is what the JWT bearer handler validates into; JwtSecurityToken is the older handler's type.
        Assert.Equal(NoonSharp, AccessTokens.IssuedAt(new JsonWebToken(withIat)));
        Assert.Equal(NoonSharp, AccessTokens.IssuedAt(new JwtSecurityTokenHandler().ReadJwtToken(withIat)));
        Assert.Equal(expires.AddDays(-60), AccessTokens.IssuedAt(new JsonWebToken(legacy)));
        Assert.Equal(expires.AddDays(-60), AccessTokens.IssuedAt(new JwtSecurityTokenHandler().ReadJwtToken(legacy)));
    }
}

/// <summary>Access tokens as the API signs them, for any issue time (or none, as before "iat" was added).</summary>
internal static class TokenFactory
{
    public const string TestKey = "token-factory-key-used-only-where-the-api-is-not-involved-0123456789";

    public static string Write(string userId, DateTime? issuedAt, DateTime expires, string key = TestKey, string issuer = "Sard", string audience = "Sardion")
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, userId),
            new(ClaimTypes.Name, "reader"),
            new(ClaimTypes.Email, "reader@example.test"),
            new("DisplayName", "reader")
        };
        if (issuedAt is { } iat)
        {
            claims.Add(new Claim(System.IdentityModel.Tokens.Jwt.JwtRegisteredClaimNames.Iat, EpochTime.GetIntDate(iat).ToString(CultureInfo.InvariantCulture), ClaimValueTypes.Integer64));
        }

        var credentials = new SigningCredentials(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(key)), SecurityAlgorithms.HmacSha256);
        return new JwtSecurityTokenHandler().WriteToken(new JwtSecurityToken(issuer, audience, claims, expires: expires, signingCredentials: credentials));
    }
}

/// <summary>The revocation check in the real pipeline: tokens from before this release (no "iat") and after it.</summary>
public class TokenRevocationHttpTests(SardApiFactory api) : IClassFixture<SardApiFactory>
{
    private static int nextIp;

    private static string NewIp()
    {
        var n = Interlocked.Increment(ref nextIp);
        return $"10.17.{n / 250}.{n % 250 + 1}";
    }

    private string Token(string userId, DateTime? issuedAt, DateTime expires) =>
        TokenFactory.Write(userId, issuedAt, expires, api.Services.GetRequiredService<IConfiguration>()["Jwt:Key"]!);

    private async Task<HttpStatusCode> MyProfile(string token)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "/api/User/my-profile");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return (await api.ClientFrom(NewIp()).SendAsync(request)).StatusCode;
    }

    private async Task<(string Id, string Name)> Register()
    {
        var name = "r" + Guid.NewGuid().ToString("N")[..10];
        using var form = new MultipartFormDataContent
        {
            { new StringContent(name), "UserName" },
            { new StringContent($"{name}@example.test"), "Email" },
            { new StringContent("Correct-horse-1"), "Password" },
            { new StringContent("قارئ"), "DisplayName" }
        };
        var registered = await api.ClientFrom(NewIp()).PostAsync("/api/identity/Register", form);
        var token = (await registered.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("accessToken").GetString()!;
        var request = new HttpRequestMessage(HttpMethod.Get, "/api/User/my-profile");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var profile = await (await api.ClientFrom(NewIp()).SendAsync(request)).Content.ReadFromJsonAsync<JsonElement>();
        return (profile.GetProperty("id").GetString()!, name);
    }

    [Fact]
    public async Task Tokens_work_until_revoked_then_only_ones_issued_since_do()
    {
        var (userId, name) = await Register();
        var now = DateTime.UtcNow;
        // As the API issued them before this release: no "iat", 60 days to live (this one issued a minute ago).
        var legacy = Token(userId, issuedAt: null, expires: now.AddDays(60).AddMinutes(-1));
        var current = Token(userId, issuedAt: now.AddMinutes(-1), expires: now.AddDays(60).AddMinutes(-1));

        // Nobody is signed out by the release itself.
        Assert.Equal(HttpStatusCode.OK, await MyProfile(legacy));
        Assert.Equal(HttpStatusCode.OK, await MyProfile(current));

        using (var scope = api.Services.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<ITokenRevocationService>().RevokeAllTokensAsync(userId);
        }

        Assert.Equal(HttpStatusCode.Unauthorized, await MyProfile(legacy));
        Assert.Equal(HttpStatusCode.Unauthorized, await MyProfile(current));

        // Signing in again works (a second later, so the new token isn't from the revoking second).
        await Task.Delay(1100);
        var login = await api.ClientFrom(NewIp()).PostAsJsonAsync("/api/identity/Login", new { loginCardinality = name, password = "Correct-horse-1" });
        var fresh = (await login.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("accessToken").GetString()!;
        Assert.Equal(HttpStatusCode.OK, await MyProfile(fresh));
        Assert.Equal(HttpStatusCode.Unauthorized, await MyProfile(legacy));
    }

    [Fact]
    public async Task A_revoked_token_on_a_public_endpoint_is_treated_as_signed_out()
    {
        var (userId, name) = await Register();
        var token = Token(userId, issuedAt: DateTime.UtcNow.AddMinutes(-1), expires: DateTime.UtcNow.AddDays(59));
        using (var scope = api.Services.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<ITokenRevocationService>().RevokeAllTokensAsync(userId);
        }

        var request = new HttpRequestMessage(HttpMethod.Get, $"/api/User/{name}");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var response = await api.ClientFrom(NewIp()).SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Tokens_of_users_that_no_longer_exist_are_refused()
    {
        var token = Token(Guid.NewGuid().ToString(), issuedAt: DateTime.UtcNow, expires: DateTime.UtcNow.AddDays(60));

        Assert.Equal(HttpStatusCode.Unauthorized, await MyProfile(token));
    }
}
