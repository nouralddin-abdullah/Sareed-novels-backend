using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Application.Users;
using Microsoft.EntityFrameworkCore;

namespace Sareed_novels_backend.Tests.Integration;

/// <summary>
/// PATCH /api/User/update-me: a field left out stays as it is, a field sent empty clears the bio and the links, and an
/// empty user name or display name is refused. The web sends the text fields in the query string (useUpdateMe.js);
/// the app may send multipart form-data. Both go through the real model binding here.
/// </summary>
[Collection(ReaderApiCollection.Name)]
public class UpdateMeHttpTests(SardApiFactory api)
{
    private const string Url = "/api/User/update-me";

    private Task<HttpResponseMessage> ByQuery(ApiUser user, params (string Name, string Value)[] fields) =>
        api.Send(HttpMethod.Patch, $"{Url}?{string.Join("&", fields.Select(f => $"{f.Name}={Uri.EscapeDataString(f.Value)}"))}", user);

    private Task<HttpResponseMessage> ByForm(ApiUser user, params (string Name, string Value)[] fields) =>
        api.Send(HttpMethod.Patch, Url, user, ReaderApi.Form(fields));

    private async Task<JsonElement> Profile(ApiUser user) => await (await api.Get("/api/User/my-profile", user)).OkJson();

    private static void AssertProfile(JsonElement profile, string? bio, string? facebook, string? twitter, string? discord)
    {
        Assert.Equal(bio, profile.GetProperty("userBio").GetString());
        Assert.Equal(facebook, profile.GetProperty("facebookUrl").GetString());
        Assert.Equal(twitter, profile.GetProperty("twitterUrl").GetString());
        Assert.Equal(discord, profile.GetProperty("discordUrl").GetString());
    }

    public static TheoryData<string> Transports => new() { "query", "form" };

    private Func<ApiUser, (string, string)[], Task<HttpResponseMessage>> Transport(string name) =>
        name == "query" ? (u, f) => ByQuery(u, f) : (u, f) => ByForm(u, f);

    [Theory]
    [MemberData(nameof(Transports))]
    public async Task Sent_values_are_set_left_out_ones_kept_and_empty_ones_cleared(string transport)
    {
        var update = Transport(transport);
        var user = await api.SignUp();

        var set = await update(user, [("UserBio", "أكتب الروايات"), ("FacebookUrl", "https://facebook.com/me"),
            ("TwitterUrl", "x.com/me"), ("DiscordUrl", "discord.gg/me")]);
        Assert.Equal(HttpStatusCode.OK, set.StatusCode);
        AssertProfile(await Profile(user), "أكتب الروايات", "https://facebook.com/me", "x.com/me", "discord.gg/me");

        var other = await update(user, [("DisplayName", "اسم جديد")]);
        Assert.Equal(HttpStatusCode.OK, other.StatusCode);
        var kept = await Profile(user);
        Assert.Equal("اسم جديد", kept.GetProperty("displayName").GetString());
        AssertProfile(kept, "أكتب الروايات", "https://facebook.com/me", "x.com/me", "discord.gg/me");

        var clearTwo = await update(user, [("UserBio", ""), ("FacebookUrl", "")]);
        Assert.Equal(HttpStatusCode.OK, clearTwo.StatusCode);
        AssertProfile(await Profile(user), null, null, "x.com/me", "discord.gg/me");

        // Field names as the web's settings page sends them (camelCase), and a blank value.
        var clearRest = await update(user, [("twitterUrl", ""), ("discordUrl", "  ")]);
        Assert.Equal(HttpStatusCode.OK, clearRest.StatusCode);
        var cleared = await Profile(user);
        AssertProfile(cleared, null, null, null, null);
        Assert.Equal("اسم جديد", cleared.GetProperty("displayName").GetString());
        Assert.Equal(user.UserName, cleared.GetProperty("userName").GetString());
    }

    [Theory]
    [MemberData(nameof(Transports))]
    public async Task An_empty_user_name_or_display_name_is_refused(string transport)
    {
        var update = Transport(transport);
        var user = await api.SignUp();
        Assert.Equal(HttpStatusCode.OK, (await update(user, [("UserBio", "نبذة")])).StatusCode);

        foreach (var field in new[] { "UserName", "DisplayName" })
        {
            foreach (var value in new[] { "", "   " })
            {
                var response = await update(user, [(field, value), ("UserBio", "")]);
                Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            }
        }

        var profile = await Profile(user);
        Assert.Equal(user.UserName, profile.GetProperty("userName").GetString());
        Assert.Equal(user.UserName, profile.GetProperty("displayName").GetString());
        Assert.Equal("نبذة", profile.GetProperty("userBio").GetString()); // nothing of a refused update applies
    }

    [Theory]
    [MemberData(nameof(Transports))]
    public async Task A_user_name_that_starts_like_a_deleted_accounts_is_refused_with_its_code(string transport)
    {
        var update = Transport(transport);
        var user = await api.SignUp();

        var response = await update(user, [("UserName", "Deleted-" + Guid.NewGuid().ToString("N")[..8]), ("UserBio", "نبذة")]);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(UserNameRules.DeletedPrefixCode, body.GetProperty("code").GetString());
        Assert.Equal(UserNameRules.DeletedPrefixMessage, body.GetProperty("message").GetString());
        var profile = await Profile(user);
        Assert.Equal(user.UserName, profile.GetProperty("userName").GetString());
        Assert.Equal(JsonValueKind.Null, profile.GetProperty("userBio").ValueKind); // nothing of a refused update applies
    }

    [Fact]
    public async Task Sign_up_refuses_a_user_name_that_starts_like_a_deleted_accounts()
    {
        var name = "deleted-" + Guid.NewGuid().ToString("N")[..8];
        using var form = ReaderApi.Form(("UserName", name), ("Email", $"{name}@example.test"), ("Password", "Correct-horse-1"),
            ("DisplayName", "قارئ جديد"));

        var response = await api.Client().PostAsync("/api/identity/Register", form);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var result = (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("result");
        Assert.Equal(UserNameRules.DeletedPrefixCode, result.GetProperty("code").GetString());
        Assert.Contains(UserNameRules.DeletedPrefixMessage, result.GetProperty("message").GetString());
        await using var db = api.Db();
        Assert.False(await db.Users.AnyAsync(u => u.UserName == name));
    }

    [Fact]
    public async Task A_json_body_is_still_not_accepted()
    {
        var user = await api.SignUp();

        var response = await api.Send(HttpMethod.Patch, Url, user, JsonContent.Create(new { userBio = "نبذة" }));

        Assert.Equal(HttpStatusCode.UnsupportedMediaType, response.StatusCode);
    }
}
