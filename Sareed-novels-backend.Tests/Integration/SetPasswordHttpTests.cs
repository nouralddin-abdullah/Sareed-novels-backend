using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Domain.Entities;
using Domain.Repositories;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Sareed_novels_backend.Tests.Integration;

/// <summary>
/// POST /api/User/set-password (#53) through the real pipeline: an account made with Google gets its first password,
/// the refusals in the order they are checked, the proof it shares with account deletion, and what it leaves alone
/// (the other sessions, the rest of the account's row, a request at the same time).
/// </summary>
public class SetPasswordHttpTests(SardApiFactory api) : IClassFixture<SardApiFactory>
{
    private const string NewPassword = "Brand-new-horse-7";

    /// <summary>The password <see cref="ReaderApi.SignUp"/> gives.</summary>
    private const string SignUpPassword = ModerationApi.Password;

    private const string PasswordAlreadySetMessage = "لحسابك كلمة مرور بالفعل، غيّرها من «تغيير كلمة المرور».";
    private const string SignInAgainMessage =
        "لتعيين كلمة مرور لحسابك سجّل الدخول بحساب Google مرة أخرى، ثم عيّنها خلال 10 دقائق";
    private const string RequiredMessage = "اكتب كلمة المرور الجديدة";
    private const string TooShortMessage = "يجب أن تحتوي كلمة المرور الجديدة على 8 أحرف على الأقل";
    private const string NoDigitMessage = "يجب أن تحتوي كلمة المرور على رقم واحد على الأقل";

    private Task<HttpResponseMessage> SetPassword(ApiUser? user, object body, HttpClient? client = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/User/set-password")
        {
            Content = JsonContent.Create(body)
        };
        if (user != null)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", user.Token);
        }
        return (client ?? api.Client()).SendAsync(request);
    }

    private Task<HttpResponseMessage> UpdatePassword(ApiUser user, string? newPassword, HttpClient? client = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Patch, "/api/User/update-password")
        {
            Content = JsonContent.Create(new { currentPassword = SignUpPassword, newPassword })
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", user.Token);
        return (client ?? api.Client()).SendAsync(request);
    }

    private Task<HttpResponseMessage> Login(string login, string password) =>
        api.Client().PostAsJsonAsync("/api/identity/Login", new { loginCardinality = login, password });

    private async Task<string> GoogleSignIn(string email, string subject, HttpClient? client = null)
    {
        var response = await (client ?? api.Client()).PostAsJsonAsync("/api/identity/google-login",
            new { idToken = api.GoogleTokens.Issue(email, subject: subject) });
        return (await response.OkJson()).GetProperty("accessToken").GetString()!;
    }

    /// <summary>A member who signed up with Google (no password), with the token of that sign-in.</summary>
    private async Task<(ApiUser User, string Email, string GoogleSubject)> SignUpWithGoogle(HttpClient? client = null)
    {
        var email = $"g{Guid.NewGuid():N}@example.test";
        var subject = "google-" + Guid.NewGuid().ToString("N");
        var token = await GoogleSignIn(email, subject, client);

        await using var db = api.Db();
        var user = await db.Users.AsNoTracking().SingleAsync(u => u.Email == email);
        Assert.Null(user.PasswordHash);
        return (new ApiUser(user.Id, user.UserName!, token), email, subject);
    }

    /// <summary>The user's access token as if issued at <paramref name="issuedAt"/>.</summary>
    private ApiUser WithTokenIssuedAt(ApiUser user, DateTime issuedAt)
    {
        var key = api.Services.GetRequiredService<IConfiguration>()["Jwt:Key"]!;
        return user with { Token = TokenFactory.Write(user.Id, issuedAt, issuedAt.AddDays(60), key) };
    }

    private async Task<User> Row(string userId)
    {
        await using var db = api.Db();
        return await db.Users.AsNoTracking().SingleAsync(u => u.Id == userId);
    }

    private async Task<bool> HasPassword(ApiUser user) =>
        (await (await api.Get("/api/User/my-profile", user)).OkJson()).GetProperty("hasPassword").GetBoolean();

    private static async Task<(string Code, string Message)> Refusal(
        HttpResponseMessage response, HttpStatusCode status)
    {
        var body = await response.Error(status);
        return (body.GetProperty("code").GetString()!, body.GetProperty("message").GetString()!);
    }

    // ─── Setting it ───

    [Fact]
    public async Task An_account_made_with_Google_sets_a_password_right_after_signing_in()
    {
        var (member, email, _) = await SignUpWithGoogle();
        Assert.False(await HasPassword(member));
        var before = await Row(member.Id);

        var response = await SetPassword(member, new { newPassword = NewPassword });

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Empty(await response.Content.ReadAsByteArrayAsync());
        Assert.True(await HasPassword(member));

        // Signing in with the email address, or the user name, and the new password works.
        var signedIn = member with
        {
            Token = (await (await Login(email, NewPassword)).OkJson()).GetProperty("accessToken").GetString()!
        };
        Assert.Equal(HttpStatusCode.OK, (await api.Get("/api/User/my-profile", signedIn)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Login(member.UserName, NewPassword)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await Login(email, "Not-the-new-one-1")).StatusCode);

        // The password and the stamps changed, nothing else: the email stays confirmed, no session was cut off.
        var after = await Row(member.Id);
        Assert.NotNull(after.PasswordHash);
        Assert.NotEqual(before.SecurityStamp, after.SecurityStamp);
        Assert.NotEqual(before.ConcurrencyStamp, after.ConcurrencyStamp);
        Assert.True(after.EmailConfirmed);
        Assert.Null(after.TokensValidAfter);
        Assert.Equal(
            (before.UserName, before.Email, before.DisplayName),
            (after.UserName, after.Email, after.DisplayName));
    }

    [Fact]
    public async Task An_older_sign_in_confirms_with_an_ID_token_of_the_same_Google_account()
    {
        var (member, email, subject) = await SignUpWithGoogle();
        var old = WithTokenIssuedAt(member, DateTime.UtcNow.AddMinutes(-11));

        var (code, message) = await Refusal(
            await SetPassword(old, new { newPassword = NewPassword }), HttpStatusCode.Forbidden);
        Assert.Equal("ReauthenticationRequired", code);
        Assert.Equal(SignInAgainMessage, message);

        var otherGoogleAccount = api.GoogleTokens.Issue(email, subject: "google-someone-else");
        (code, message) = await Refusal(
            await SetPassword(old, new { newPassword = NewPassword, googleIdToken = otherGoogleAccount }),
            HttpStatusCode.Forbidden);
        Assert.Equal("ReauthenticationFailed", code);
        Assert.Equal("حساب Google هذا غير مرتبط بحسابك في سرد", message);

        (code, message) = await Refusal(
            await SetPassword(old, new { newPassword = NewPassword, googleIdToken = "not-a-google-token" }),
            HttpStatusCode.Forbidden);
        Assert.Equal("ReauthenticationFailed", code);
        Assert.Equal("تعذّر التحقق من حساب Google، سجّل الدخول به مرة أخرى", message);

        Assert.Null((await Row(member.Id)).PasswordHash);

        var sameGoogleAccount = api.GoogleTokens.Issue(email, subject: subject);
        var response = await SetPassword(old, new { newPassword = NewPassword, googleIdToken = sameGoogleAccount });
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.True(await HasPassword(member));
        Assert.Equal(HttpStatusCode.OK, (await Login(email, NewPassword)).StatusCode);

        // Nine minutes is still recent.
        var (other, _, _) = await SignUpWithGoogle();
        var nineMinutes = WithTokenIssuedAt(other, DateTime.UtcNow.AddMinutes(-9));
        var recentEnough = await SetPassword(nineMinutes, new { newPassword = NewPassword });
        Assert.Equal(HttpStatusCode.NoContent, recentEnough.StatusCode);
    }

    // ─── Refusals, in the order they are checked ───

    [Fact]
    public async Task Signing_in_is_needed()
    {
        var response = await SetPassword(null, new { newPassword = NewPassword });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task An_account_with_a_password_is_told_so_first_and_nothing_changes()
    {
        var member = await api.SignUp();
        var before = await Row(member.Id);
        var old = WithTokenIssuedAt(member, DateTime.UtcNow.AddHours(-1));

        // Before the rules (a password they refuse, or none) and before the proof (an old sign-in).
        foreach (var (caller, body) in new (ApiUser, object)[]
                 {
                     (member, new { newPassword = NewPassword }),
                     (member, new { newPassword = "short" }),
                     (member, new { }),
                     (old, new { newPassword = NewPassword }),
                     (old, new { newPassword = "short" })
                 })
        {
            var (code, message) = await Refusal(await SetPassword(caller, body), HttpStatusCode.BadRequest);
            Assert.Equal("PasswordAlreadySet", code);
            Assert.Equal(PasswordAlreadySetMessage, message);
        }

        var after = await Row(member.Id);
        Assert.Equal(before.PasswordHash, after.PasswordHash);
        Assert.Equal(before.SecurityStamp, after.SecurityStamp);
        Assert.Equal(before.ConcurrencyStamp, after.ConcurrencyStamp);
        Assert.Equal(HttpStatusCode.OK, (await Login(member.UserName, SignUpPassword)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await Login(member.UserName, NewPassword)).StatusCode);
    }

    [Fact]
    public async Task The_rules_are_update_passwords_with_its_code_and_message_and_come_before_the_proof()
    {
        var (member, _, _) = await SignUpWithGoogle();
        // With a sign-in this old the proof would be refused (403): the rules answer first.
        var old = WithTokenIssuedAt(member, DateTime.UtcNow.AddMinutes(-30));
        var withPassword = await api.SignUp();

        foreach (var (password, expected) in new (string?, string)[]
                 {
                     (null, RequiredMessage), ("", TooShortMessage), ("Short-7", TooShortMessage)
                 })
        {
            foreach (var caller in new[] { member, old })
            {
                var (code, message) = await Refusal(
                    await SetPassword(caller, new { newPassword = password }), HttpStatusCode.BadRequest);
                Assert.Equal(("ValidationFailed", expected), (code, message));
            }

            // update-password refuses the same new password with the same code and message.
            var change = await Refusal(await UpdatePassword(withPassword, password), HttpStatusCode.BadRequest);
            Assert.Equal(("ValidationFailed", expected), change);
        }

        // A body without the field is a password left out.
        var missing = await Refusal(await SetPassword(member, new { }), HttpStatusCode.BadRequest);
        Assert.Equal(("ValidationFailed", RequiredMessage), missing);

        Assert.False(await HasPassword(member));

        // Eight characters are enough.
        var eight = await SetPassword(member, new { newPassword = "Eight-88" });
        Assert.Equal(HttpStatusCode.NoContent, eight.StatusCode);
    }

    [Fact]
    public async Task Identitys_own_password_rules_answer_as_update_password_answers_them()
    {
        // With today's options Identity refuses nothing the rule above lets through; stricter ones show it still runs.
        await using var strict = api.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
            services.Configure<IdentityOptions>(options => options.Password.RequireDigit = true)));
        var client = strict.CreateClient();
        var (member, _, _) = await SignUpWithGoogle(client);
        var old = WithTokenIssuedAt(member, DateTime.UtcNow.AddMinutes(-30));
        var withPassword = await api.SignUp();

        foreach (var caller in new[] { member, old })
        {
            var response = await SetPassword(caller, new { newPassword = "no-digits-here" }, client);
            var body = await response.Error(HttpStatusCode.BadRequest);
            Assert.Equal("PasswordRequiresDigit", body.GetProperty("code").GetString());
            Assert.Equal(NoDigitMessage, body.GetProperty("message").GetString());
            Assert.False(body.GetProperty("succeeded").GetBoolean());
            Assert.Equal("PasswordRequiresDigit", body.GetProperty("errors")[0].GetProperty("code").GetString());
        }

        var changed = await UpdatePassword(withPassword, "no-digits-here", client);
        var change = await changed.Error(HttpStatusCode.BadRequest);
        Assert.Equal("PasswordRequiresDigit", change.GetProperty("code").GetString());
        Assert.Equal(NoDigitMessage, change.GetProperty("message").GetString());

        Assert.Null((await Row(member.Id)).PasswordHash);
        var withDigit = await SetPassword(member, new { newPassword = "with-a-digit-1" }, client);
        Assert.Equal(HttpStatusCode.NoContent, withDigit.StatusCode);
    }

    [Fact]
    public async Task One_address_gets_ten_requests_a_minute()
    {
        var client = api.ClientFrom("10.53.0.1");
        var statuses = new List<HttpStatusCode>();
        for (var i = 0; i < 11; i++)
        {
            statuses.Add((await SetPassword(null, new { newPassword = NewPassword }, client)).StatusCode);
        }

        Assert.All(statuses.Take(10), status => Assert.Equal(HttpStatusCode.Unauthorized, status));
        Assert.Equal(HttpStatusCode.TooManyRequests, statuses[10]);
    }

    // ─── What it leaves alone ───

    [Fact]
    public async Task Every_session_stays_signed_in()
    {
        // The phone's session registered it for push notifications; the web signed in with the same Google account.
        var (phone, email, subject) = await SignUpWithGoogle();
        var device = new { token = "fcm-" + Guid.NewGuid().ToString("N"), platform = "android" };
        var registered = await api.Send(
            HttpMethod.Post, "/api/notifications/devices", phone, JsonContent.Create(device));
        Assert.Equal(HttpStatusCode.NoContent, registered.StatusCode);
        var web = phone with { Token = await GoogleSignIn(email, subject) };

        Assert.Equal(HttpStatusCode.NoContent, (await SetPassword(web, new { newPassword = NewPassword })).StatusCode);

        foreach (var session in new[] { phone, web })
        {
            Assert.Equal(HttpStatusCode.OK, (await api.Get("/api/User/my-profile", session)).StatusCode);
        }
        Assert.Null((await Row(phone.Id)).TokensValidAfter);
        await using var db = api.Db();
        Assert.True(await db.UserDevices.AnyAsync(d => d.UserId == phone.Id));
    }

    [Fact]
    public async Task A_reset_link_sent_before_stops_working()
    {
        var (member, email, _) = await SignUpWithGoogle();
        string earlierLink;
        using (var scope = api.Services.CreateScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<User>>();
            earlierLink = await users.GeneratePasswordResetTokenAsync((await users.FindByIdAsync(member.Id))!);
        }

        var set = await SetPassword(member, new { newPassword = NewPassword });
        Assert.Equal(HttpStatusCode.NoContent, set.StatusCode);

        var reset = await api.Client().PostAsJsonAsync("/api/identity/reset-password",
            new { userId = member.Id, token = earlierLink, newPassword = "Someone-elses-9" });
        Assert.Equal("InvalidToken", (await Refusal(reset, HttpStatusCode.BadRequest)).Code);
        Assert.Equal(HttpStatusCode.OK, (await Login(email, NewPassword)).StatusCode);
    }

    [Fact]
    public async Task Requests_at_the_same_time_set_one_password()
    {
        var (member, email, _) = await SignUpWithGoogle();
        var passwords = Enumerable.Range(1, 4).Select(i => $"Racing-horse-{i}").ToArray();

        var responses = await Task.WhenAll(
            passwords.Select(password => SetPassword(member, new { newPassword = password })));

        var winner = Assert.Single(
            passwords.Zip(responses), pair => pair.Second.StatusCode == HttpStatusCode.NoContent).First;
        foreach (var refused in responses.Where(r => r.StatusCode != HttpStatusCode.NoContent))
        {
            Assert.Equal("PasswordAlreadySet", (await Refusal(refused, HttpStatusCode.BadRequest)).Code);
        }
        Assert.Equal(HttpStatusCode.OK, (await Login(email, winner)).StatusCode);
        foreach (var loser in passwords.Where(p => p != winner))
        {
            Assert.Equal(HttpStatusCode.Forbidden, (await Login(email, loser)).StatusCode);
        }
    }

    [Fact]
    public async Task The_save_writes_only_the_password_and_the_stamps_and_only_while_there_is_none()
    {
        var (member, _, _) = await SignUpWithGoogle();
        var before = await Row(member.Id);
        using var scope = api.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<User>>();
        var repository = scope.ServiceProvider.GetRequiredService<IUsersRepository>();
        // What a request that read the account before (update-me) holds.
        var readBefore = (await users.FindByIdAsync(member.Id))!;

        // Meanwhile counters and the wallet change in SQL, as they do elsewhere.
        await using (var db = api.Db())
        {
            await db.Users.Where(u => u.Id == member.Id).ExecuteUpdateAsync(s => s
                .SetProperty(u => u.LibraryNovelsCount, 7)
                .SetProperty(u => u.ReviewsCount, 3)
                .SetProperty(u => u.PointBalance, 50m));
        }

        Assert.True(await repository.SetFirstPasswordAsync(member.Id, "first-hash"));
        Assert.False(await repository.SetFirstPasswordAsync(member.Id, "second-hash"));

        var row = await Row(member.Id);
        Assert.Equal("first-hash", row.PasswordHash);
        Assert.Equal((7, 3, 50m), (row.LibraryNovelsCount, row.ReviewsCount, row.PointBalance));
        Assert.NotEqual(before.SecurityStamp, row.SecurityStamp);
        Assert.NotEqual(before.ConcurrencyStamp, row.ConcurrencyStamp);

        // That request's save fails on the new concurrency stamp instead of writing the row back without the password.
        readBefore.UserBio = "نبذة";
        var update = await users.UpdateAsync(readBefore);
        Assert.Equal("ConcurrencyFailure", Assert.Single(update.Errors).Code);
        Assert.Equal("first-hash", (await Row(member.Id)).PasswordHash);

        // A deleted account (no password either) never gets one.
        var (deleted, _, _) = await SignUpWithGoogle();
        await using (var db = api.Db())
        {
            await db.Users.Where(u => u.Id == deleted.Id)
                .ExecuteUpdateAsync(s => s.SetProperty(u => u.DeletedAt, DateTime.UtcNow));
        }
        Assert.False(await repository.SetFirstPasswordAsync(deleted.Id, "a-hash"));
        Assert.Null((await Row(deleted.Id)).PasswordHash);
    }
}
