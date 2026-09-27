using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Domain.Constants;
using Domain.Entities;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Sareed_novels_backend.Tests.Integration;

/// <summary>Reports, blocks and admins through the API under test, and the content they act on.</summary>
internal static class ModerationApi
{
    public const string Password = "Correct-horse-1";

    /// <summary>A new user made admin, with a token issued after that (so it carries the role).</summary>
    public static async Task<ApiUser> SignUpAdmin(this SardApiFactory api)
    {
        var user = await api.SignUp();
        using (var scope = api.Services.CreateScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<User>>();
            Assert.True((await users.AddToRoleAsync((await users.FindByIdAsync(user.Id))!, UserRoles.Admin)).Succeeded);
        }
        return user with { Token = await api.SignIn(user.UserName) };
    }

    public static Task<HttpResponseMessage> Login(this SardApiFactory api, string userName) =>
        api.Client().PostAsJsonAsync("/api/identity/Login", new { loginCardinality = userName, password = Password });

    public static async Task<string> SignIn(this SardApiFactory api, string userName) =>
        (await (await api.Login(userName)).OkJson()).GetProperty("accessToken").GetString()!;

    public static Task<HttpResponseMessage> Report(this SardApiFactory api, ApiUser reporter, string targetType, object targetId,
        string reason = "Spam", string? details = null) =>
        api.Send(HttpMethod.Post, "/api/reports", reporter,
            JsonContent.Create(new { targetType, targetId = targetId.ToString(), reason, details }));

    /// <summary>Reports and returns the new report's id (201).</summary>
    public static async Task<Guid> Reported(this SardApiFactory api, ApiUser reporter, string targetType, object targetId, string reason = "Spam")
    {
        var response = await api.Report(reporter, targetType, targetId, reason);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.OkJson()).GetProperty("id").GetGuid();
    }

    public static Task<HttpResponseMessage> Resolve(this SardApiFactory api, ApiUser admin, Guid reportId, string action, int? suspensionDays = null) =>
        api.Send(HttpMethod.Patch, $"/api/admin/reports/{reportId}", admin,
            JsonContent.Create(suspensionDays is null ? new { action } : (object)new { action, suspensionDays }));

    public static async Task<JsonElement> AdminReports(this SardApiFactory api, ApiUser admin, string? status = null, int pageSize = 100)
    {
        var query = status is null ? $"?pageSize={pageSize}" : $"?status={status}&pageSize={pageSize}";
        return await (await api.Get($"/api/admin/reports{query}", admin)).OkJson();
    }

    public static Task<HttpResponseMessage> Block(this SardApiFactory api, ApiUser blocker, ApiUser blocked) =>
        api.Send(HttpMethod.Post, "/api/User/block", blocker, JsonContent.Create(new { userId = blocked.Id }));

    public static Task<HttpResponseMessage> Unblock(this SardApiFactory api, ApiUser blocker, ApiUser blocked) =>
        api.Send(HttpMethod.Delete, "/api/User/unblock", blocker, JsonContent.Create(new { userId = blocked.Id }));

    public static Task<HttpResponseMessage> Follow(this SardApiFactory api, ApiUser follower, ApiUser followed) =>
        api.Send(HttpMethod.Post, "/api/User/follow", follower, JsonContent.Create(new { userIdToFollow = followed.Id }));

    /// <summary>A comment (or, with <paramref name="parentId"/>, a reply) posted through the API at <paramref name="url"/>.</summary>
    public static async Task<Guid> Comment(this SardApiFactory api, ApiUser author, string url, string content = "تعليق", Guid? parentId = null)
    {
        var fields = parentId is { } parent ? new[] { ("Content", content), ("ParentCommentId", parent.ToString()) } : [("Content", content)];
        var response = await api.Send(HttpMethod.Post, url, author, ReaderApi.Form(fields));
        return (await response.OkJson()).GetProperty("comment").GetProperty("id").GetGuid();
    }

    public static async Task<Guid> Post(this SardApiFactory api, ApiUser author, string content = "منشور")
    {
        var response = await api.Send(HttpMethod.Post, "/api/posts", author, ReaderApi.Form(("Content", content)));
        return (await response.OkJson()).GetProperty("post").GetProperty("id").GetGuid();
    }

    public static async Task<Guid> Review(this SardApiFactory api, ApiUser reviewer, Guid novelId, int score = 4, string content = "رواية جميلة جداً")
    {
        var response = await api.Send(HttpMethod.Post, $"/api/{novelId}", reviewer, JsonContent.Create(new
        {
            writingQualityScore = score, updatingStabilityScore = score, characterDevelopmentScore = score, worldBuildingScore = score,
            isSpoiler = false, content
        }));
        return (await response.OkJson()).GetProperty("review").GetProperty("id").GetGuid();
    }

    public static async Task<Guid> ReadingList(this SardApiFactory api, ApiUser owner, bool isPublic = true, string name = "قائمتي")
    {
        var response = await api.Send(HttpMethod.Post, "/api/readinglist", owner,
            ReaderApi.Form(("Name", name + " " + Seed.Marker()), ("IsPublic", isPublic ? "true" : "false")));
        return (await response.OkJson()).GetProperty("readingList").GetProperty("id").GetGuid();
    }

    /// <summary>The ids of a page's items.</summary>
    public static List<Guid> Ids(this JsonElement page, string list = "items", string id = "id") =>
        page.GetProperty(list).EnumerateArray().Select(item => item.GetProperty(id).GetGuid()).ToList();

    /// <summary>The JSON error body of a response that must have this status.</summary>
    public static async Task<JsonElement> Error(this HttpResponseMessage response, HttpStatusCode expected)
    {
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(expected == response.StatusCode, $"expected {(int)expected}, got {(int)response.StatusCode}: {body}");
        return JsonDocument.Parse(body).RootElement.Clone();
    }

    /// <summary>The notifications a user has from an actor, straight from the database.</summary>
    public static async Task<List<Notification>> NotificationsFrom(this SardApiFactory api, ApiUser recipient, ApiUser actor)
    {
        await using var db = api.Db();
        return await db.Notifications.AsNoTracking().Where(n => n.UserId == recipient.Id && n.ActorId == actor.Id).ToListAsync();
    }

    /// <summary>Waits (up to 15 s) for the fire-and-forget notification work of an API call to show up.</summary>
    public static async Task<List<Notification>> WaitForNotificationsFrom(this SardApiFactory api, ApiUser recipient, ApiUser actor, int count = 1)
    {
        for (var waited = 0; waited < 150; waited++)
        {
            var found = await api.NotificationsFrom(recipient, actor);
            if (found.Count >= count)
            {
                return found;
            }
            await Task.Delay(100);
        }
        throw new TimeoutException($"{recipient.UserName} got no {count} notification(s) from {actor.UserName}");
    }
}
