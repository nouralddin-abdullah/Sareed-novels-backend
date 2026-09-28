using System.Net;
using Domain.Constants;
using Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Sareed_novels_backend.Tests.Integration;

/// <summary>PATCH /api/notifications/read-all and /{id}/read: idempotent, 204 whether or not anything was unread.</summary>
[Collection(ReaderApiCollection.Name)]
public class NotificationReadHttpTests(SardApiFactory api)
{
    private static Notification Notice(ApiUser to, ApiUser from, bool isRead = false) => new()
    {
        Id = Guid.NewGuid(),
        UserId = to.Id,
        Type = NotificationType.NewFollower,
        ActorId = from.Id,
        ActorDisplayName = from.UserName,
        Message = "بدأ بمتابعتك",
        ActionUrl = $"/profile/{from.UserName}",
        IsRead = isRead,
        CreatedAt = DateTime.UtcNow
    };

    private async Task<List<Notification>> Seed(params Notification[] notices)
    {
        await using var db = api.Db();
        db.Notifications.AddRange(notices);
        await db.SaveChangesAsync();
        return notices.ToList();
    }

    private async Task<int> UnreadCount(ApiUser user) =>
        (await (await api.Get("/api/notifications/unread-count", user)).OkJson()).GetProperty("unreadCount").GetInt32();

    [Fact]
    public async Task Mark_all_read_with_nothing_unread_succeeds_every_time()
    {
        // It used to answer 400 "Failed to mark all notifications as read" when there was nothing to mark.
        var user = await api.SignUp();

        for (var i = 0; i < 3; i++)
        {
            var response = await api.Send(HttpMethod.Patch, "/api/notifications/read-all", user);
            Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        }
        Assert.Equal(0, await UnreadCount(user));
    }

    [Fact]
    public async Task Mark_all_read_marks_only_the_callers_unread_notifications()
    {
        var user = await api.SignUp();
        var other = await api.SignUp();
        var mine = await Seed(Notice(user, other), Notice(user, other), Notice(user, other, isRead: true));
        var theirs = await Seed(Notice(other, user));
        Assert.Equal(2, await UnreadCount(user));

        Assert.Equal(HttpStatusCode.NoContent, (await api.Send(HttpMethod.Patch, "/api/notifications/read-all", user)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await api.Send(HttpMethod.Patch, "/api/notifications/read-all", user)).StatusCode);

        Assert.Equal(0, await UnreadCount(user));
        Assert.Equal(1, await UnreadCount(other));
        await using var db = api.Db();
        var ids = mine.Select(n => n.Id).ToList();
        Assert.All(await db.Notifications.Where(n => ids.Contains(n.Id)).ToListAsync(), n => Assert.True(n.IsRead));
        Assert.False((await db.Notifications.SingleAsync(n => n.Id == theirs[0].Id)).IsRead);
    }

    [Fact]
    public async Task Marking_one_read_twice_succeeds_and_someone_elses_is_refused()
    {
        var user = await api.SignUp();
        var other = await api.SignUp();
        var (mine, theirs) = (Notice(user, other), Notice(other, user));
        await Seed(mine, theirs);

        Assert.Equal(HttpStatusCode.NoContent, (await api.Send(HttpMethod.Patch, $"/api/notifications/{mine.Id}/read", user)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await api.Send(HttpMethod.Patch, $"/api/notifications/{mine.Id}/read", user)).StatusCode);
        Assert.Equal(0, await UnreadCount(user));

        var refused = await api.Send(HttpMethod.Patch, $"/api/notifications/{theirs.Id}/read", user);
        Assert.Equal(HttpStatusCode.Forbidden, refused.StatusCode);
        Assert.Equal(1, await UnreadCount(other));
    }
}
