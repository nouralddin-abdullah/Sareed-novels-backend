using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Application.Users;
using Application.Users.DTOS;
using Infrastructure.Authorization;
using Sareed_novels_backend.Middlewares;

namespace Sareed_novels_backend.Tests.Integration;

/// <summary>
/// GET /api/User/username-available (#69): whether the signed-in member could take a user name now, answered with the
/// code of the rule it breaks and update-me's message, as PATCH update-me would refuse it.
/// </summary>
[Collection(ReaderApiCollection.Name)]
public class UserNameAvailabilityHttpTests(SardApiFactory api)
{
    private const string Url = "/api/User/username-available";
    private static int nextIp;

    /// <summary>An address of its own for the rate limit's tests (ReaderApi's clients use 10.14.x.x).</summary>
    private static string NewIp()
    {
        var n = Interlocked.Increment(ref nextIp);
        return $"10.69.{n / 250}.{n % 250 + 1}";
    }

    private static string NewName() => "n" + Guid.NewGuid().ToString("N")[..10];

    /// <summary>Identity's refusal of a character it doesn't allow, as the API describes it.</summary>
    private static readonly string InvalidCharacters = new ArabicIdentityErrorDescriber().InvalidUserName(null).Description;

    private static string Query(string? userName) => userName is null ? Url : $"{Url}?userName={Uri.EscapeDataString(userName)}";

    private async Task<JsonElement> Check(ApiUser user, string? userName) => await (await api.Get(Query(userName), user)).OkJson();

    private static void AssertAnswer(JsonElement body, bool available, string? code, string message)
    {
        string[] fields = ["available", "code", "message"];
        Assert.Equal(fields, body.EnumerateObject().Select(p => p.Name));
        Assert.Equal(available, body.GetProperty("available").GetBoolean());
        Assert.Equal(code, body.GetProperty("code").GetString());
        Assert.Equal(message, body.GetProperty("message").GetString());
    }

    private static void AssertAvailable(JsonElement body) => AssertAnswer(body, true, null, UserNameAvailabilityDto.AvailableMessage);

    private Task<HttpResponseMessage> UpdateMe(ApiUser user, string userName) =>
        api.Send(HttpMethod.Patch, "/api/User/update-me", user, ReaderApi.Form(("UserName", userName)));

    [Fact]
    public async Task A_free_name_and_the_callers_own_in_any_letter_case_are_available()
    {
        var user = await api.SignUp();

        AssertAvailable(await Check(user, NewName()));
        AssertAvailable(await Check(user, user.UserName));
        AssertAvailable(await Check(user, user.UserName.ToUpperInvariant()));
    }

    [Fact]
    public async Task Another_members_name_in_any_letter_case_is_taken_as_update_me_says()
    {
        var (user, other) = (await api.SignUp(), await api.SignUp());

        foreach (var name in new[] { other.UserName, other.UserName.ToUpperInvariant() })
        {
            AssertAnswer(await Check(user, name), false, UserNameRules.TakenCode, UserNameRules.TakenMessage);
            var refused = await (await UpdateMe(user, name)).Error(HttpStatusCode.BadRequest);
            Assert.Equal(UserNameRules.TakenCode, refused.GetProperty("code").GetString());
            Assert.Equal(UserNameRules.TakenMessage, refused.GetProperty("message").GetString());
        }
    }

    public static TheoryData<string, string, string> Refusals() => new()
    {
        { "   ", UserNameRules.InvalidCode, UserNameRules.RequiredMessage },
        { "ab", UserNameRules.InvalidCode, UserNameRules.LengthMessage },
        { new string('a', 21), UserNameRules.InvalidCode, UserNameRules.LengthMessage },
        { "a@", UserNameRules.InvalidCode, UserNameRules.LengthMessage }, // update-me's first message
        { "me@example", UserNameRules.InvalidCode, UserNameRules.NoAtSignMessage },
        { "noor ali", UserNameRules.InvalidCode, InvalidCharacters },
        { "noor!", UserNameRules.InvalidCode, InvalidCharacters },
        { "نور الهدى", UserNameRules.InvalidCode, InvalidCharacters },
        { "blocked", UserNameRules.ReservedCode, UserNameRules.ReservedMessage },
        { "My-Profile", UserNameRules.ReservedCode, UserNameRules.ReservedMessage },
        { "username-available", UserNameRules.ReservedCode, UserNameRules.ReservedMessage },
        { "USERNAME-AVAILABLE", UserNameRules.ReservedCode, UserNameRules.ReservedMessage },
        { "following-list", UserNameRules.ReservedCode, UserNameRules.ReservedMessage },
        { "deleted-3f2a9c1b7d4e", UserNameRules.ReservedCode, UserNameRules.DeletedPrefixMessage },
        { "Deleted-Noor", UserNameRules.ReservedCode, UserNameRules.DeletedPrefixMessage },
    };

    [Theory]
    [MemberData(nameof(Refusals))]
    public async Task Each_refusal_has_its_rules_code_and_the_message_update_me_refuses_the_name_with(string userName, string code, string message)
    {
        var user = await api.SignUp();

        AssertAnswer(await Check(user, userName), false, code, message);

        // update-me refuses the same name with the same message: its validator's as ValidationFailed, Identity's after
        // «تعذّر تحديث الملف الشخصي», the others with the same code.
        var refused = await (await UpdateMe(user, userName)).Error(HttpStatusCode.BadRequest);
        Assert.Contains(message, refused.GetProperty("message").GetString());
        Assert.Contains(refused.GetProperty("code").GetString(), new[] { code, ValidationProblems.Code });
    }

    [Fact]
    public async Task A_missing_or_empty_name_is_invalid()
    {
        var user = await api.SignUp();

        AssertAnswer(await Check(user, null), false, UserNameRules.InvalidCode, UserNameRules.RequiredMessage);
        AssertAnswer(await Check(user, ""), false, UserNameRules.InvalidCode, UserNameRules.RequiredMessage);
    }

    [Fact]
    public async Task A_name_another_member_gave_up_is_free_as_update_me_lets_one_take_it()
    {
        var (user, renamed) = (await api.SignUp(), await api.SignUp());
        Assert.Equal(HttpStatusCode.OK, (await UpdateMe(renamed, NewName())).StatusCode);

        AssertAvailable(await Check(user, renamed.UserName));

        Assert.Equal(HttpStatusCode.OK, (await UpdateMe(user, renamed.UserName)).StatusCode);
        // The member who holds the name now has its profile; the one who gave it up keeps the new one.
        var profile = await (await api.Get($"/api/User/{renamed.UserName}")).OkJson();
        Assert.Equal(user.Id, profile.GetProperty("id").GetString());
    }

    [Fact]
    public async Task The_callers_name_is_read_from_the_account_not_the_token()
    {
        // The token still carries the name the member gave up, which someone else took since.
        var (user, other) = (await api.SignUp(), await api.SignUp());
        var newName = NewName();
        Assert.Equal(HttpStatusCode.OK, (await UpdateMe(user, newName)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await UpdateMe(other, user.UserName)).StatusCode);

        AssertAnswer(await Check(user, user.UserName), false, UserNameRules.TakenCode, UserNameRules.TakenMessage);
        AssertAvailable(await Check(user, newName.ToUpperInvariant()));
    }

    [Fact]
    public async Task Username_available_is_reserved_and_its_route_is_the_check_not_a_profile()
    {
        var user = await api.SignUp();

        // Signed out it is 401, not a profile's 404; signed in, the check's answer.
        Assert.Equal(HttpStatusCode.Unauthorized, (await api.Get(Query("someone"))).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await api.Get(Url)).StatusCode);
        AssertAnswer(await Check(user, null), false, UserNameRules.InvalidCode, UserNameRules.RequiredMessage);

        // Nobody can take the name: update-me and sign-up refuse it.
        var renamed = await (await UpdateMe(user, "username-available")).Error(HttpStatusCode.BadRequest);
        Assert.Equal(UserNameRules.ReservedMessage, renamed.GetProperty("message").GetString());
        var signUp = await api.Client().PostAsync("/api/identity/Register", ReaderApi.Form(("UserName", "Username-Available"),
            ("Email", $"{NewName()}@example.test"), ("Password", "Correct-horse-1"), ("DisplayName", "قارئ جديد")));
        Assert.Equal(HttpStatusCode.BadRequest, signUp.StatusCode);
        Assert.Contains(UserNameRules.ReservedMessage, await signUp.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task One_address_gets_60_checks_a_minute()
    {
        var user = await api.SignUp();
        var client = api.ClientFrom(NewIp());
        var statuses = new List<HttpStatusCode>();
        HttpResponseMessage? last = null;
        for (var i = 0; i < 61; i++)
        {
            var request = new HttpRequestMessage(HttpMethod.Get, Query(NewName()));
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", user.Token);
            last = await client.SendAsync(request);
            statuses.Add(last.StatusCode);
        }

        Assert.All(statuses.Take(60), s => Assert.Equal(HttpStatusCode.OK, s));
        var limited = await last!.Error(HttpStatusCode.TooManyRequests);
        Assert.Equal("TooManyRequests", limited.GetProperty("code").GetString());

        // Another address is unaffected.
        var other = new HttpRequestMessage(HttpMethod.Get, Query(NewName()));
        other.Headers.Authorization = new AuthenticationHeaderValue("Bearer", user.Token);
        Assert.Equal(HttpStatusCode.OK, (await api.ClientFrom(NewIp()).SendAsync(other)).StatusCode);
    }
}
