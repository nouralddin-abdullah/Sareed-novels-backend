using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Application.Services;
using Application.Users;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace Sareed_novels_backend.Tests.Integration;

/// <summary>
/// PATCH /api/User/update-me: a field left out stays as it is, a field sent empty clears the bio and the links, and an
/// empty user name or display name is refused. The web sends the text fields in the query string (useUpdateMe.js);
/// the app may send multipart form-data. Both go through the real model binding here. A success answers with the saved
/// profile, exactly as GET my-profile returns it after the save (#44); a refusal answers as before, without one.
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

    /// <summary>
    /// The profile a successful update answers with (#44), once it is checked to be the JSON GET my-profile returns
    /// right after: the same fields with the same values.
    /// </summary>
    private async Task<JsonElement> SavedProfile(HttpResponseMessage response, ApiUser user)
    {
        var body = await response.OkJson();
        string[] fields = ["success", "message", "profile"];
        Assert.Equal(fields, body.EnumerateObject().Select(p => p.Name));
        Assert.True(body.GetProperty("success").GetBoolean());
        Assert.Equal("تم تحديث الملف الشخصي", body.GetProperty("message").GetString());
        var profile = body.GetProperty("profile");
        Assert.Equal((await Profile(user)).GetRawText(), profile.GetRawText());
        return profile;
    }

    /// <summary>A refusal as update-me answered it before #44: these fields and no others (no profile).</summary>
    private static void AssertRefusal(JsonElement body, string code, params string[] moreFields)
    {
        string[] fields = ["success", "code", "message", .. moreFields];
        Assert.Equal(fields, body.EnumerateObject().Select(p => p.Name));
        Assert.False(body.GetProperty("success").GetBoolean());
        Assert.Equal(code, body.GetProperty("code").GetString());
    }

    private static MultipartFormDataContent WithImage(MultipartFormDataContent form, string field)
    {
        var image = new ByteArrayContent([0x89, 0x50, 0x4E, 0x47]);
        image.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        form.Add(image, field, field + ".png");
        return form;
    }

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

    [Theory]
    [MemberData(nameof(Transports))]
    public async Task Changing_only_the_case_of_ones_own_user_name_is_allowed(string transport)
    {
        // It was refused as taken: the lookup ignores case and found the member themselves (#25).
        var update = Transport(transport);
        var user = await api.SignUp();
        var recased = user.UserName.ToUpperInvariant();

        var response = await update(user, [("UserName", recased)]);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(recased, (await Profile(user)).GetProperty("userName").GetString());
        await using var db = api.Db();
        Assert.False(await db.UserNameChanges.AnyAsync(c => c.UserId == user.Id)); // not a new name: nothing to redirect
    }

    [Fact]
    public async Task Another_members_name_in_any_case_is_taken_and_identitys_refusals_keep_their_code()
    {
        var (user, other) = (await api.SignUp(), await api.SignUp());

        var taken = await (await ByForm(user, ("UserName", other.UserName.ToUpperInvariant()))).Error(HttpStatusCode.BadRequest);
        Assert.Equal("UserNameTaken", taken.GetProperty("code").GetString());

        // Arabic letters pass the length and reserved-name checks, and Identity refuses them: its code, not OperationFailed.
        var invalid = await (await ByForm(user, ("UserName", "اسم عربي"))).Error(HttpStatusCode.BadRequest);
        Assert.Equal("InvalidUserName", invalid.GetProperty("code").GetString());
        Assert.StartsWith("تعذّر تحديث الملف الشخصي", invalid.GetProperty("message").GetString());
        Assert.False(invalid.GetProperty("success").GetBoolean());
        Assert.Equal(user.UserName, (await Profile(user)).GetProperty("userName").GetString());
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

    [Theory]
    [MemberData(nameof(Transports))]
    public async Task A_new_user_name_comes_back_in_the_answer_as_my_profile_shows_it(string transport)
    {
        var update = Transport(transport);
        var (user, follower) = (await api.SignUp(), await api.SignUp());
        (await api.Follow(follower, user)).EnsureSuccessStatusCode();
        var name = "n" + Guid.NewGuid().ToString("N")[..10];

        // The token still has the old name: the profile is the account's, read after the save.
        var renamed = await SavedProfile(await update(user, [("UserName", name), ("DisplayName", "اسم جديد")]), user);
        var recased = await SavedProfile(await update(user, [("UserName", name.ToUpperInvariant())]), user);

        Assert.Equal(name, renamed.GetProperty("userName").GetString());
        Assert.Equal("اسم جديد", renamed.GetProperty("displayName").GetString());
        Assert.Equal(1, renamed.GetProperty("totalFollowers").GetInt32());
        Assert.Equal(name.ToUpperInvariant(), recased.GetProperty("userName").GetString());
    }

    [Theory]
    [MemberData(nameof(Transports))]
    public async Task A_bio_set_or_cleared_comes_back_in_the_answer_as_my_profile_shows_it(string transport)
    {
        var update = Transport(transport);
        var user = await api.SignUp();

        var set = await SavedProfile(await update(user, [("UserBio", "أكتب الروايات")]), user);
        var cleared = await SavedProfile(await update(user, [("UserBio", "")]), user);

        Assert.Equal("أكتب الروايات", set.GetProperty("userBio").GetString());
        Assert.Equal(JsonValueKind.Null, cleared.GetProperty("userBio").ValueKind);
    }

    [Theory]
    [MemberData(nameof(Transports))]
    public async Task Each_link_set_or_cleared_comes_back_in_the_answer_as_my_profile_shows_it(string transport)
    {
        var update = Transport(transport);
        var user = await api.SignUp();
        (string Field, string Property, string Value)[] links =
            [("FacebookUrl", "facebookUrl", "https://facebook.com/me"), ("TwitterUrl", "twitterUrl", "x.com/me"), ("DiscordUrl", "discordUrl", "discord.gg/me")];

        foreach (var (field, property, value) in links)
        {
            var set = await SavedProfile(await update(user, [(field, value)]), user);
            Assert.Equal(value, set.GetProperty(property).GetString());
        }
        foreach (var (field, property, _) in links)
        {
            var cleared = await SavedProfile(await update(user, [(field, "")]), user);
            Assert.Equal(JsonValueKind.Null, cleared.GetProperty(property).ValueKind);
        }
    }

    [Fact]
    public async Task A_new_photo_or_banner_comes_back_in_the_answer_as_my_profile_shows_it()
    {
        var user = await api.SignUp();

        var photo = await SavedProfile(await api.Send(HttpMethod.Patch, Url, user, WithImage(ReaderApi.Form(), "ProfilePhoto")), user);
        var banner = await SavedProfile(await api.Send(HttpMethod.Patch, Url, user, WithImage(ReaderApi.Form(), "ProfileBanner")), user);

        Assert.StartsWith($"https://files.test/profile-images/{user.Id}/", photo.GetProperty("profilePhoto").GetString());
        Assert.StartsWith($"https://files.test/profile-banners/{user.Id}/", banner.GetProperty("profileBanner").GetString());
        Assert.Equal(photo.GetProperty("profilePhoto").GetString(), banner.GetProperty("profilePhoto").GetString());
    }

    [Fact]
    public async Task Refusals_answer_as_before_without_a_profile()
    {
        var (user, other) = (await api.SignUp(), await api.SignUp());

        var taken = await (await ByForm(user, ("UserName", other.UserName))).Error(HttpStatusCode.BadRequest);
        var deleted = await (await ByForm(user, ("UserName", "deleted-" + Guid.NewGuid().ToString("N")[..8]))).Error(HttpStatusCode.BadRequest);
        var identity = await (await ByForm(user, ("UserName", "اسم عربي"))).Error(HttpStatusCode.BadRequest);
        var invalid = await (await ByForm(user, ("UserBio", new string('ن', 151)))).Error(HttpStatusCode.BadRequest);

        AssertRefusal(taken, "UserNameTaken");
        AssertRefusal(deleted, UserNameRules.DeletedPrefixCode);
        AssertRefusal(identity, "InvalidUserName");
        // The validators' refusal is still the validation problem.
        Assert.Equal("ValidationFailed", invalid.GetProperty("code").GetString());
        Assert.True(invalid.GetProperty("errors").TryGetProperty("UserBio", out _));
        Assert.False(invalid.TryGetProperty("profile", out _));
    }

    [Fact]
    public async Task A_failed_upload_answers_as_before_without_a_profile()
    {
        var uploads = Substitute.For<IFileUploadService>();
        uploads.UploadImageAsync(Arg.Any<Stream>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>())
            .ThrowsAsync(new IOException("R2 is down"));
        await using var broken = api.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IFileUploadService>();
            services.AddSingleton(uploads);
        }));
        var user = await api.SignUp();
        using var request = new HttpRequestMessage(HttpMethod.Patch, Url)
        {
            Content = WithImage(ReaderApi.Form(("UserBio", "نبذة")), "ProfilePhoto")
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", user.Token);

        var refused = await (await broken.CreateClient().SendAsync(request)).Error(HttpStatusCode.BadRequest);

        AssertRefusal(refused, "UploadFailed", "field");
        Assert.Equal("ProfilePhoto", refused.GetProperty("field").GetString());
        Assert.Equal(JsonValueKind.Null, (await Profile(user)).GetProperty("userBio").ValueKind); // nothing was saved
    }

    [Fact]
    public async Task Signed_out_is_401()
    {
        var response = await api.Send(HttpMethod.Patch, Url, null, ReaderApi.Form(("UserBio", "نبذة")));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
