using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Application.Services;
using Application.Users.Commands.GoogleLogin;
using Domain.Entities;
using Google.Apis.Auth;
using Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Sareed_novels_backend.Tests.Integration;

/// <summary>ID tokens "issued by Google" for tests: whatever payload the test registered for the token.</summary>
public sealed class FakeGoogleIdTokens : IGoogleIdTokenValidator
{
    private readonly ConcurrentDictionary<string, GoogleJsonWebSignature.Payload> issued = new();

    public string Issue(string email, bool emailVerified = true, string? name = "قارئة من Google", string? subject = null)
    {
        var token = "test-google-id-token-" + Guid.NewGuid().ToString("N");
        issued[token] = new GoogleJsonWebSignature.Payload
        {
            Subject = subject ?? "google-" + Guid.NewGuid().ToString("N"),
            Email = email,
            EmailVerified = emailVerified,
            Name = name,
        };
        return token;
    }

    public Task<GoogleJsonWebSignature.Payload> ValidateAsync(string idToken) =>
        issued.TryGetValue(idToken, out var payload)
            ? Task.FromResult(payload)
            : throw new InvalidJwtException("Not a token this test issued");
}

/// <summary>
/// Google sign-in through the real API pipeline (POST /api/identity/google-login, which the web's callback also
/// ends in), including the pre-hijack case: an account registered with someone else's unverified address.
/// </summary>
public class GoogleSignInTests(SardApiFactory api) : IClassFixture<SardApiFactory>
{
    private const string AttackersPassword = "attackers-password-1";
    private static int nextIp;

    private static string NewIp()
    {
        var n = Interlocked.Increment(ref nextIp);
        return $"10.16.{n / 250}.{n % 250 + 1}";
    }

    private static string NewEmail() => $"reader-{Guid.NewGuid():N}@example.test";

    private static string NewName() => "g" + Guid.NewGuid().ToString("N")[..10];

    /// <summary>The sign-in's answer: the token and flags, or the {code, message} of a refusal.</summary>
    private async Task<JsonElement> GoogleLogin(string idToken, HttpStatusCode expected = HttpStatusCode.OK)
    {
        var response = await api.ClientFrom(NewIp()).PostAsJsonAsync("/api/identity/google-login", new { idToken });
        Assert.Equal(expected, response.StatusCode);
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    private async Task<string> Register(string userName, string email, string password)
    {
        using var form = new MultipartFormDataContent
        {
            { new StringContent(userName), "UserName" },
            { new StringContent(email), "Email" },
            { new StringContent(password), "Password" },
            { new StringContent("قارئ"), "DisplayName" }
        };
        var response = await api.ClientFrom(NewIp()).PostAsync("/api/identity/Register", form);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("accessToken").GetString()!;
    }

    private async Task<HttpStatusCode> Login(string login, string password) =>
        (await api.ClientFrom(NewIp()).PostAsJsonAsync("/api/identity/Login", new { loginCardinality = login, password })).StatusCode;

    private async Task<HttpResponseMessage> MyProfile(string token)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "/api/User/my-profile");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return await api.ClientFrom(NewIp()).SendAsync(request);
    }

    private async Task<(User User, IList<UserLoginInfo> Logins)> Account(string email)
    {
        using var scope = api.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<User>>();
        var user = await users.FindByEmailAsync(email);
        Assert.NotNull(user);
        return (user, await users.GetLoginsAsync(user));
    }

    private async Task Update(string email, Func<IQueryable<User>, Task> update)
    {
        using var scope = api.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await update(db.Users.Where(u => u.Email == email));
    }

    private async Task<bool> Emailed(string to, string template)
    {
        // The notice is sent off the request.
        for (var i = 0; i < 50; i++)
        {
            if (api.Emails.Sent.Any(e => e.To == to && e.Template == template))
            {
                return true;
            }
            await Task.Delay(100);
        }
        return false;
    }

    [Fact]
    public async Task A_new_Google_user_gets_a_confirmed_account_whose_names_never_come_from_the_email()
    {
        var email = NewEmail();
        var named = await GoogleLogin(api.GoogleTokens.Issue(email, name: "ليلى"));
        var unnamedEmail = NewEmail();
        var unnamed = await GoogleLogin(api.GoogleTokens.Issue(unnamedEmail, name: null));

        Assert.False(named.GetProperty("passwordReset").GetBoolean());
        var profile = await (await MyProfile(named.GetProperty("accessToken").GetString()!)).Content.ReadFromJsonAsync<JsonElement>();
        Assert.Matches("^sarduser[0-9]{6}$", profile.GetProperty("userName").GetString());
        Assert.Equal("ليلى", profile.GetProperty("displayName").GetString());

        var (user, logins) = await Account(email);
        Assert.True(user.EmailConfirmed);
        Assert.Single(logins, l => l.LoginProvider == "Google");

        // No name from Google: the generated handle, not the address, is the display name.
        var unnamedProfile = await (await MyProfile(unnamed.GetProperty("accessToken").GetString()!)).Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(unnamedProfile.GetProperty("userName").GetString(), unnamedProfile.GetProperty("displayName").GetString());
        Assert.DoesNotContain("@", unnamedProfile.GetRawText());
    }

    [Fact]
    public async Task A_verified_account_is_linked_and_keeps_its_password_and_sessions()
    {
        var email = NewEmail();
        var name = NewName();
        var ownToken = await Register(name, email, AttackersPassword);
        await Update(email, users => users.ExecuteUpdateAsync(s => s.SetProperty(u => u.EmailConfirmed, true)));

        var result = await GoogleLogin(api.GoogleTokens.Issue(email));

        Assert.False(result.GetProperty("passwordReset").GetBoolean());
        Assert.Equal(HttpStatusCode.OK, await Login(name, AttackersPassword));
        Assert.Equal(HttpStatusCode.OK, (await MyProfile(ownToken)).StatusCode);
        var (user, logins) = await Account(email);
        Assert.NotNull(user.PasswordHash);
        Assert.Null(user.TokensValidAfter);
        Assert.Single(logins, l => l.LoginProvider == "Google");
        Assert.DoesNotContain(api.Emails.Sent, e => e.To == email);
    }

    [Fact]
    public async Task Google_sign_in_hands_an_account_registered_with_the_owners_unverified_address_to_the_owner()
    {
        // The attacker registers the victim's address with a password of their own (sign-up doesn't verify it),
        // signs in, and even links a Google account of their own.
        var victimEmail = NewEmail();
        var attackerName = NewName();
        var attackersToken = await Register(attackerName, victimEmail, AttackersPassword);
        using (var scope = api.Services.CreateScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<User>>();
            var account = (await users.FindByEmailAsync(victimEmail))!;
            Assert.True((await users.AddLoginAsync(account, new UserLoginInfo("Google", "attackers-google-" + Guid.NewGuid(), "Google"))).Succeeded);
        }
        await Update(victimEmail, users => users.ExecuteUpdateAsync(s => s
            .SetProperty(u => u.AccessFailedCount, 3)
            .SetProperty(u => u.LockoutEnd, DateTimeOffset.UtcNow.AddMinutes(5))));
        Assert.Equal(HttpStatusCode.OK, (await MyProfile(attackersToken)).StatusCode);

        // Tokens carry whole seconds and one issued in the revoking second stays valid: let a second pass.
        await Task.Delay(1100);
        var victimsSubject = "victims-google-" + Guid.NewGuid();
        var result = await GoogleLogin(api.GoogleTokens.Issue(victimEmail, subject: victimsSubject));

        Assert.True(result.GetProperty("passwordReset").GetBoolean());
        var victimsToken = result.GetProperty("accessToken").GetString()!;

        // The attacker's password and session are gone...
        Assert.Equal(HttpStatusCode.Forbidden, await Login(victimEmail, AttackersPassword));
        Assert.Equal(HttpStatusCode.Forbidden, await Login(attackerName, AttackersPassword));
        Assert.Equal(HttpStatusCode.Unauthorized, (await MyProfile(attackersToken)).StatusCode);

        // ...and the victim is in, on the same account.
        var mine = await MyProfile(victimsToken);
        Assert.Equal(HttpStatusCode.OK, mine.StatusCode);
        var (user, logins) = await Account(victimEmail);
        Assert.Equal(user.Id, (await mine.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetString());
        Assert.True(user.EmailConfirmed);
        Assert.Null(user.PasswordHash);
        Assert.Null(user.LockoutEnd);
        Assert.NotNull(user.TokensValidAfter);
        var login = Assert.Single(logins);
        Assert.Equal(("Google", victimsSubject), (login.LoginProvider, login.ProviderKey));
        Assert.True(await Emailed(victimEmail, GoogleLoginCommandHandler.PasswordRemovedEmailTemplate));

        // The address now counts as verified: signing in with Google again changes nothing.
        var again = await GoogleLogin(api.GoogleTokens.Issue(victimEmail, subject: victimsSubject));
        Assert.False(again.GetProperty("passwordReset").GetBoolean());
        Assert.Equal(HttpStatusCode.OK, (await MyProfile(victimsToken)).StatusCode);
    }

    [Fact]
    public async Task An_unverified_account_without_a_password_is_confirmed_without_a_password_notice()
    {
        // Google sign-ups from before unverified Google addresses were refused have no password and an unconfirmed address.
        var email = NewEmail();
        await Register(NewName(), email, AttackersPassword);
        await Update(email, users => users.ExecuteUpdateAsync(s => s.SetProperty(u => u.PasswordHash, (string?)null)));

        var result = await GoogleLogin(api.GoogleTokens.Issue(email));

        Assert.False(result.GetProperty("passwordReset").GetBoolean());
        Assert.True((await Account(email)).User.EmailConfirmed);
        Assert.DoesNotContain(api.Emails.Sent, e => e.To == email);
    }

    [Fact]
    public async Task An_address_Google_has_not_verified_is_refused()
    {
        var email = NewEmail();
        var refused = await GoogleLogin(api.GoogleTokens.Issue(email, emailVerified: false), HttpStatusCode.Forbidden);
        Assert.Equal(GoogleLoginCommandHandler.EmailNotVerifiedCode, refused.GetProperty("code").GetString());
        Assert.Equal(GoogleLoginCommandHandler.EmailNotVerifiedMessage, refused.GetProperty("message").GetString());

        var unverifiedForExisting = NewEmail();
        await Register(NewName(), unverifiedForExisting, AttackersPassword);
        await GoogleLogin(api.GoogleTokens.Issue(unverifiedForExisting, emailVerified: false), HttpStatusCode.Forbidden);

        using var scope = api.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<User>>();
        Assert.Null(await users.FindByEmailAsync(email));
        var existing = (await users.FindByEmailAsync(unverifiedForExisting))!;
        Assert.False(existing.EmailConfirmed);
        Assert.NotNull(existing.PasswordHash);
        Assert.Empty(await users.GetLoginsAsync(existing));
    }

    [Fact]
    public async Task A_token_Google_did_not_issue_is_refused()
    {
        var refused = await GoogleLogin("forged-id-token", HttpStatusCode.Forbidden);

        Assert.Equal(GoogleLoginCommandHandler.InvalidTokenCode, refused.GetProperty("code").GetString());
        Assert.Equal(GoogleLoginCommandHandler.InvalidTokenMessage, refused.GetProperty("message").GetString());
    }

    /// <summary>A user holding <paramref name="userName"/>, created directly.</summary>
    private async Task Occupy(string userName)
    {
        using var scope = api.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<User>>();
        var holder = new User { UserName = userName, Email = NewEmail(), DisplayName = userName, CreatedAt = DateTime.UtcNow };
        Assert.True((await users.CreateAsync(holder)).Succeeded);
    }

    [Fact]
    public async Task A_new_Google_user_whose_handle_is_taken_gets_the_next_one()
    {
        // Used to be an InvalidOperationException, answered 500 "Something went wrong".
        var subject = "google-" + Guid.NewGuid().ToString("N");
        var first = GoogleLoginCommandHandler.CandidateUserName(subject, 0);
        await Occupy(first);

        var result = await GoogleLogin(api.GoogleTokens.Issue(NewEmail(), subject: subject));

        var profile = await (await MyProfile(result.GetProperty("accessToken").GetString()!)).Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(GoogleLoginCommandHandler.CandidateUserName(subject, 1), profile.GetProperty("userName").GetString());
        Assert.NotEqual(first, profile.GetProperty("userName").GetString());
    }

    [Fact]
    public async Task When_every_handle_it_tries_is_taken_sign_in_is_refused_with_a_code_not_a_server_error()
    {
        var subject = "google-" + Guid.NewGuid().ToString("N");
        for (var attempt = 0; attempt < GoogleLoginCommandHandler.UserNameAttempts; attempt++)
        {
            await Occupy(GoogleLoginCommandHandler.CandidateUserName(subject, attempt));
        }
        var email = NewEmail();

        var refused = await GoogleLogin(api.GoogleTokens.Issue(email, subject: subject), HttpStatusCode.BadRequest);

        Assert.Equal(GoogleLoginCommandHandler.SignInFailedCode, refused.GetProperty("code").GetString());
        Assert.Matches(@"\p{IsArabic}", refused.GetProperty("message").GetString()!);
        using var scope = api.Services.CreateScope();
        Assert.Null(await scope.ServiceProvider.GetRequiredService<UserManager<User>>().FindByEmailAsync(email));
    }

    [Fact]
    public async Task Candidate_handles_are_six_digit_sard_handles_that_differ_per_attempt()
    {
        var candidates = Enumerable.Range(0, GoogleLoginCommandHandler.UserNameAttempts)
            .Select(attempt => GoogleLoginCommandHandler.CandidateUserName("same-subject", attempt))
            .ToList();

        Assert.All(candidates, name => Assert.Matches("^sarduser[1-9][0-9]{5}$", name));
        Assert.Equal(candidates.Count, candidates.Distinct().Count());
        Assert.Equal(candidates[0], GoogleLoginCommandHandler.CandidateUserName("same-subject", 0));
    }
}
