using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Application.Users;
using Domain.Entities;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Sareed_novels_backend.Tests.Integration;

/// <summary>Old profile links (/profile/{old user name}) after a rename, and user names with "@".</summary>
public class UserNameHistoryHttpTests(SardApiFactory api) : IClassFixture<SardApiFactory>
{
    private static int nextIp;

    private static string NewIp()
    {
        var n = Interlocked.Increment(ref nextIp);
        return $"10.18.{n / 250}.{n % 250 + 1}";
    }

    private static string NewName() => "n" + Guid.NewGuid().ToString("N")[..10];

    private async Task<HttpResponseMessage> Register(string userName)
    {
        using var form = new MultipartFormDataContent
        {
            { new StringContent(userName), "UserName" },
            { new StringContent($"member-{Guid.NewGuid():N}@example.test"), "Email" },
            { new StringContent("Correct-horse-1"), "Password" },
            { new StringContent("عضو سرد"), "DisplayName" }
        };
        return await api.ClientFrom(NewIp()).PostAsync("/api/identity/Register", form);
    }

    private async Task<string> RegisterOk(string userName)
    {
        var response = await Register(userName);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("accessToken").GetString()!;
    }

    private async Task<HttpResponseMessage> Rename(string token, string newUserName)
    {
        var request = new HttpRequestMessage(HttpMethod.Patch, $"/api/User/update-me?UserName={Uri.EscapeDataString(newUserName)}");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return await api.ClientFrom(NewIp()).SendAsync(request);
    }

    private async Task<(HttpStatusCode Status, JsonElement Body)> Profile(string userName)
    {
        var response = await api.ClientFrom(NewIp()).GetAsync($"/api/User/{Uri.EscapeDataString(userName)}");
        return (response.StatusCode, response.IsSuccessStatusCode ? await response.Content.ReadFromJsonAsync<JsonElement>() : default);
    }

    [Fact]
    public async Task A_renamed_member_is_found_under_every_old_name_with_the_current_one_in_the_profile()
    {
        var first = NewName();
        var second = NewName();
        var third = NewName();
        var token = await RegisterOk(first);

        Assert.Equal(HttpStatusCode.OK, (await Rename(token, second)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Rename(token, third)).StatusCode);

        foreach (var name in new[] { first, second, third, second.ToUpperInvariant() })
        {
            var (status, profile) = await Profile(name);
            Assert.Equal(HttpStatusCode.OK, status);
            Assert.Equal(third, profile.GetProperty("userName").GetString());
        }

        var lists = await api.ClientFrom(NewIp()).GetAsync($"/api/readinglist/user/{first}");
        Assert.Equal(HttpStatusCode.OK, lists.StatusCode);

        using var scope = api.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var changes = await db.UserNameChanges.OrderBy(c => c.Id)
            .Where(c => c.OldUserName == first || c.OldUserName == second).ToListAsync();
        Assert.Equal([first, second], changes.Select(c => c.OldUserName));
        Assert.Equal([first.ToUpperInvariant(), second.ToUpperInvariant()], changes.Select(c => c.OldNormalizedUserName));
    }

    [Fact]
    public async Task An_email_shaped_name_from_before_the_fix_finds_the_member()
    {
        // As the data fix leaves it: the member has a handle now, and the address they used as user name is history.
        var handle = NewName();
        await RegisterOk(handle);
        var oldName = $"{handle}.old@gmail.com";
        using (var scope = api.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var user = await db.Users.SingleAsync(u => u.UserName == handle);
            db.UserNameChanges.Add(new UserNameChange
            {
                UserId = user.Id,
                OldUserName = oldName,
                OldNormalizedUserName = oldName.ToUpperInvariant(),
                ChangedAt = DateTime.UtcNow
            });
            await db.SaveChangesAsync();
        }

        var (status, profile) = await Profile(oldName);

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal(handle, profile.GetProperty("userName").GetString());
        Assert.DoesNotContain("@", profile.GetRawText());
    }

    [Fact]
    public async Task A_member_who_holds_a_name_now_wins_over_one_who_gave_it_up()
    {
        var shared = NewName();
        var token = await RegisterOk(shared);
        var renamedTo = NewName();
        Assert.Equal(HttpStatusCode.OK, (await Rename(token, renamedTo)).StatusCode);
        Assert.Equal(renamedTo, (await Profile(shared)).Body.GetProperty("userName").GetString());

        await RegisterOk(shared);

        Assert.Equal(shared, (await Profile(shared)).Body.GetProperty("userName").GetString());
        Assert.Equal(renamedTo, (await Profile(renamedTo)).Body.GetProperty("userName").GetString());
    }

    [Fact]
    public async Task Unknown_names_are_not_found()
    {
        Assert.Equal(HttpStatusCode.NotFound, (await Profile(NewName())).Status);
        Assert.Equal(HttpStatusCode.NotFound, (await api.ClientFrom(NewIp()).GetAsync($"/api/readinglist/user/{NewName()}")).StatusCode);
    }

    [Fact]
    public async Task User_names_with_an_at_sign_are_refused_at_sign_up_and_in_update_me()
    {
        var signUp = await Register($"{NewName()}@gmail.com");
        Assert.Equal(HttpStatusCode.BadRequest, signUp.StatusCode);
        Assert.Contains(UserNameRules.NoAtSignMessage, await signUp.Content.ReadAsStringAsync());

        var name = NewName();
        var token = await RegisterOk(name);
        var rename = await Rename(token, "me@gmail.com");
        Assert.Equal(HttpStatusCode.BadRequest, rename.StatusCode);
        Assert.Contains(UserNameRules.NoAtSignMessage, await rename.Content.ReadAsStringAsync());
        Assert.Equal(name, (await Profile(name)).Body.GetProperty("userName").GetString());
    }
}
