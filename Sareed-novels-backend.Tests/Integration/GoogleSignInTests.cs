using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Application.Services;
using Application.Users;
using Application.Users.Commands.GoogleLogin;
using Domain.Entities;
using Google.Apis.Auth;
using Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Sareed_novels_backend.Tests.Integration;

/// <summary>ID tokens "issued by Google" for tests: whatever payload the test registered for the token.</summary>
public sealed class FakeGoogleIdTokens : IGoogleIdTokenValidator
{
    private readonly ConcurrentDictionary<string, GoogleJsonWebSignature.Payload> issued = new();

    public string Issue(string email, bool emailVerified = true, string? name = "قارئة من Google", string? subject = null,
        string? givenName = null, string? familyName = null)
    {
        var token = "test-google-id-token-" + Guid.NewGuid().ToString("N");
        issued[token] = new GoogleJsonWebSignature.Payload
        {
            Subject = subject ?? "google-" + Guid.NewGuid().ToString("N"),
            Email = email,
            EmailVerified = emailVerified,
            Name = name,
            GivenName = givenName,
            FamilyName = familyName,
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
/// ends in), including the pre-hijack case: an account registered with someone else's unverified address; and a new
/// account's handle from the Google name, with isNewAccount (#69).
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
        Assert.True(named.GetProperty("isNewAccount").GetBoolean());
        Assert.True(unnamed.GetProperty("isNewAccount").GetBoolean());
        var profile = await (await MyProfile(named.GetProperty("accessToken").GetString()!)).Content.ReadFromJsonAsync<JsonElement>();
        // An Arabic name gives no handle (#69): the "sarduser" one.
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
        Assert.False(result.GetProperty("isNewAccount").GetBoolean()); // linked, not created
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
        Assert.False(result.GetProperty("isNewAccount").GetBoolean()); // handed over, not created
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
        var first = GoogleUserNames.Fallback(subject, 0);
        await Occupy(first);

        var result = await GoogleLogin(api.GoogleTokens.Issue(NewEmail(), subject: subject));

        var profile = await (await MyProfile(result.GetProperty("accessToken").GetString()!)).Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(GoogleUserNames.Fallback(subject, 1), profile.GetProperty("userName").GetString());
        Assert.NotEqual(first, profile.GetProperty("userName").GetString());
    }

    [Fact]
    public async Task When_every_handle_it_tries_is_taken_sign_in_is_refused_with_a_code_not_a_server_error()
    {
        var subject = "google-" + Guid.NewGuid().ToString("N");
        for (var attempt = 0; attempt < GoogleUserNames.FallbackAttempts; attempt++)
        {
            await Occupy(GoogleUserNames.Fallback(subject, attempt));
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
        var candidates = Enumerable.Range(0, GoogleUserNames.FallbackAttempts)
            .Select(attempt => GoogleUserNames.Fallback("same-subject", attempt))
            .ToList();

        Assert.All(candidates, name => Assert.Matches("^sarduser[1-9][0-9]{5}$", name));
        Assert.Equal(candidates.Count, candidates.Distinct().Count());
        Assert.Equal(candidates[0], GoogleUserNames.Fallback("same-subject", 0));
    }

    // ---- The handle from the Google name, and isNewAccount (#69) ----

    private async Task<JsonElement> MyProfile(JsonElement signIn) =>
        await (await MyProfile(signIn.GetProperty("accessToken").GetString()!)).Content.ReadFromJsonAsync<JsonElement>();

    /// <summary>A Latin name no other test uses ("Reader" and eight hex digits), and the handle it gives.</summary>
    private static (string Name, string Handle) UniqueLatinName()
    {
        var id = Guid.NewGuid().ToString("N")[..8];
        return ($"Reader {id.ToUpperInvariant()}", $"reader-{id}");
    }

    [Fact]
    public async Task A_new_account_takes_its_handle_from_a_latin_Google_name_and_only_the_sign_in_that_made_it_is_new()
    {
        var email = NewEmail();
        var subject = "google-" + Guid.NewGuid().ToString("N");

        var first = await GoogleLogin(api.GoogleTokens.Issue(email, name: "Shahd Elattar", subject: subject));
        var again = await GoogleLogin(api.GoogleTokens.Issue(email, name: "Shahd Elattar", subject: subject));

        string[] fields = ["accessToken", "expiresFor", "passwordReset", "isNewAccount"];
        Assert.Equal(fields, first.EnumerateObject().Select(p => p.Name));
        Assert.True(first.GetProperty("isNewAccount").GetBoolean());
        Assert.False(again.GetProperty("isNewAccount").GetBoolean());
        var profile = await MyProfile(first);
        Assert.Equal("shahd-elattar", profile.GetProperty("userName").GetString());
        Assert.Equal("Shahd Elattar", profile.GetProperty("displayName").GetString());
        Assert.Equal(profile.GetProperty("id").GetString(), (await MyProfile(again)).GetProperty("id").GetString());

        // Someone else with the same Google name.
        var namesake = await GoogleLogin(api.GoogleTokens.Issue(NewEmail(), name: "Shahd Elattar"));
        Assert.True(namesake.GetProperty("isNewAccount").GetBoolean());
        Assert.Equal("shahd-elattar-2", (await MyProfile(namesake)).GetProperty("userName").GetString());
    }

    [Fact]
    public async Task Signing_in_with_a_password_is_never_a_new_account()
    {
        var email = NewEmail();
        var name = NewName();
        await Register(name, email, AttackersPassword);

        foreach (var login in new[] { email, name })
        {
            var response = await api.ClientFrom(NewIp()).PostAsJsonAsync("/api/identity/Login",
                new { loginCardinality = login, password = AttackersPassword });
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.False((await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("isNewAccount").GetBoolean());
        }
    }

    [Theory]
    [InlineData("شهد العطار")]
    [InlineData("Shahd شهد")]
    [InlineData("✨🌸")]
    public async Task A_name_that_gives_no_handle_makes_a_new_account_with_a_sarduser_one(string name)
    {
        var subject = "google-" + Guid.NewGuid().ToString("N");

        var result = await GoogleLogin(api.GoogleTokens.Issue(NewEmail(), name: name, subject: subject));

        Assert.True(result.GetProperty("isNewAccount").GetBoolean());
        Assert.Equal(GoogleUserNames.Fallback(subject, 0), (await MyProfile(result)).GetProperty("userName").GetString());
    }

    [Fact]
    public async Task Without_a_full_name_the_handle_comes_from_the_given_and_family_names()
    {
        var family = "Q" + Guid.NewGuid().ToString("N")[..8];

        var result = await GoogleLogin(api.GoogleTokens.Issue(NewEmail(), name: null, givenName: "Noor", familyName: family));

        Assert.Equal($"noor-{family.ToLowerInvariant()}", (await MyProfile(result)).GetProperty("userName").GetString());
    }

    [Fact]
    public async Task A_taken_handle_gets_the_next_free_number_and_past_twenty_a_sarduser_one()
    {
        // Held in another letter case: user names are unique whatever their case.
        var (name, handle) = UniqueLatinName();
        await Occupy(handle.ToUpperInvariant());
        await Occupy(handle + "-2");

        var third = await GoogleLogin(api.GoogleTokens.Issue(NewEmail(), name: name));

        Assert.Equal(handle + "-3", (await MyProfile(third)).GetProperty("userName").GetString());

        var (crowded, crowdedHandle) = UniqueLatinName();
        foreach (var taken in GoogleUserNames.Numbered(crowdedHandle))
        {
            await Occupy(taken);
        }
        var subject = "google-" + Guid.NewGuid().ToString("N");

        var fallback = await GoogleLogin(api.GoogleTokens.Issue(NewEmail(), name: crowded, subject: subject));

        Assert.True(fallback.GetProperty("isNewAccount").GetBoolean());
        Assert.Equal(GoogleUserNames.Fallback(subject, 0), (await MyProfile(fallback)).GetProperty("userName").GetString());
    }

    /// <summary>
    /// Holds the new accounts of these addresses once the validators passed them, until all of them got there: none of
    /// them existed when each was validated, so their inserts race for the same user name. Records the user names it
    /// validates (signing in validates again when it adds the Google login).
    /// </summary>
    private sealed class SimultaneousSignUps(params string[] emails) : IUserValidator<User>
    {
        private readonly TaskCompletionSource allValidated = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int validated;

        public ConcurrentQueue<string> UserNames { get; } = new();

        public async Task<IdentityResult> ValidateAsync(UserManager<User> manager, User user)
        {
            if (emails.Contains(user.Email))
            {
                UserNames.Enqueue(user.UserName!);
                if (Interlocked.Increment(ref validated) == emails.Length)
                {
                    allValidated.TrySetResult();
                }
                await allValidated.Task.WaitAsync(TimeSpan.FromSeconds(30));
            }
            return IdentityResult.Success;
        }
    }

    [Fact]
    public async Task Two_first_sign_ins_with_the_same_name_at_once_both_get_an_account()
    {
        var (name, handle) = UniqueLatinName();
        string[] emails = [NewEmail(), NewEmail()];
        var race = new SimultaneousSignUps(emails);
        await using var racing = api.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
            services.AddSingleton<IUserValidator<User>>(race)));
        var client = racing.CreateClient();

        var responses = await Task.WhenAll(emails.Select(email =>
            client.PostAsJsonAsync("/api/identity/google-login", new { idToken = api.GoogleTokens.Issue(email, name: name) })));

        foreach (var response in responses)
        {
            var body = await response.Content.ReadAsStringAsync();
            Assert.True(response.StatusCode == HttpStatusCode.OK, $"{(int)response.StatusCode}: {body}");
            Assert.True(JsonDocument.Parse(body).RootElement.GetProperty("isNewAccount").GetBoolean());
        }
        // Both accounts were validated with the same free handle before either was saved: the unique index refused the
        // second insert, and that sign-in went on with the next handle.
        Assert.Equal([handle, handle], race.UserNames.Take(2));
        var userNames = new List<string>();
        foreach (var email in emails)
        {
            userNames.Add((await Account(email)).User.UserName!);
        }
        Assert.Equal([handle, handle + "-2"], userNames.Order());
    }
}
