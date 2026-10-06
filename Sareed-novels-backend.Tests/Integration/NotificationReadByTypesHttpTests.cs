using System.Net;
using Application.Notifications.Commands.MarkTypesAsRead;
using Domain.Constants;
using Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Sareed_novels_backend.Tests.Integration;

/// <summary>
/// PATCH /api/notifications/read?types=… (#92), «على رواياتي»'s «تحديد الكل كمقروء»: it marks the caller's unread
/// notifications of those types read and no others, answers how many and the unread counts left, and never widens to
/// every type. The parsing alone: Unit/NotificationTypeFilterTests.
/// </summary>
[Collection(ReaderApiCollection.Name)]
public class NotificationReadByTypesHttpTests(SardApiFactory api)
{
    /// <summary>What the app sends for «على رواياتي».</summary>
    private const string OnMyNovels = "CommentOnChapter,ReviewOnNovel,GiftReceived,PrivilegeSubscribed";

    private static readonly string[] AnswerFields = ["marked", "unreadCount", "typesUnreadCount"];

    private sealed record Inbox(ApiUser User, Dictionary<string, Notification> ByName);

    /// <summary>
    /// The inbox of the list's tests (NotificationTypesHttpTests): of «على رواياتي»'s types n1, n2, n4, n5, n7 and n8, of
    /// which n1, n4, n7 and n8 are unread; unread of the other types: n3 (NewFollower) and n6 (LikeOnComment).
    /// </summary>
    private async Task<Inbox> SeedInbox()
    {
        var (user, actor) = (await api.SignUp(), await api.SignUp());
        (string Type, bool IsRead)[] rows =
        [
            (NotificationType.CommentOnChapter, false), // n1
            (NotificationType.ReviewOnNovel, true), // n2
            (NotificationType.NewFollower, false), // n3
            (NotificationType.GiftReceived, false), // n4
            (NotificationType.CommentOnChapter, true), // n5
            (NotificationType.LikeOnComment, false), // n6
            (NotificationType.PrivilegeSubscribed, false), // n7
            (NotificationType.ReviewOnNovel, false), // n8
            (NotificationType.NewFollower, true), // n9
        ];
        var start = DateTime.UtcNow.AddHours(-1);
        var notices = rows.Select((row, i) => Notice(user, actor, row.Type, row.IsRead, start.AddSeconds(i))).ToList();

        await using var db = api.Db();
        db.Notifications.AddRange(notices);
        await db.SaveChangesAsync();
        return new Inbox(user, notices.Select((n, i) => ($"n{i + 1}", n)).ToDictionary(p => p.Item1, p => p.n));
    }

    private static Notification Notice(ApiUser to, ApiUser from, string type, bool isRead, DateTime at) => new()
    {
        Id = Guid.NewGuid(),
        UserId = to.Id,
        Type = type,
        ActorId = from.Id,
        ActorDisplayName = from.UserName,
        Message = "إشعار",
        ActionUrl = $"/profile/{from.UserName}",
        IsRead = isRead,
        CreatedAt = at
    };

    private sealed record Answer(int Marked, int UnreadCount, int TypesUnreadCount);

    /// <summary>PATCH read with this query string, checked to answer 200 with exactly the issue's fields.</summary>
    private async Task<Answer> MarkRead(ApiUser user, string query)
    {
        var body = await (await api.Send(HttpMethod.Patch, $"/api/notifications/read{query}", user)).OkJson();
        Assert.Equal(AnswerFields, body.EnumerateObject().Select(p => p.Name));
        return new Answer(body.GetProperty("marked").GetInt32(), body.GetProperty("unreadCount").GetInt32(),
            body.GetProperty("typesUnreadCount").GetInt32());
    }

    private async Task<int> UnreadCount(ApiUser user, string query = "") =>
        (await (await api.Get($"/api/notifications/unread-count{query}", user)).OkJson()).GetProperty("unreadCount").GetInt32();

    /// <summary>The names of the inbox's notifications that are unread now, from the database.</summary>
    private async Task<List<string>> Unread(Inbox inbox)
    {
        await using var db = api.Db();
        var unread = await db.Notifications.AsNoTracking().Where(n => n.UserId == inbox.User.Id && !n.IsRead).Select(n => n.Id).ToListAsync();
        return inbox.ByName.Where(p => unread.Contains(p.Value.Id)).Select(p => p.Key).Order().ToList();
    }

    [Fact]
    public async Task It_marks_only_those_types_and_answers_the_unread_counts_left()
    {
        var inbox = await SeedInbox();

        var answer = await MarkRead(inbox.User, $"?types={OnMyNovels}");

        Assert.Equal(new Answer(Marked: 4, UnreadCount: 2, TypesUnreadCount: 0), answer);
        Assert.Equal(["n3", "n6"], await Unread(inbox));
        Assert.Equal(2, await UnreadCount(inbox.User));
        Assert.Equal(0, await UnreadCount(inbox.User, $"?types={OnMyNovels}"));

        // Again: nothing left of those types, the same counts.
        Assert.Equal(new Answer(Marked: 0, UnreadCount: 2, TypesUnreadCount: 0), await MarkRead(inbox.User, $"?types={OnMyNovels}"));
        Assert.Equal(["n3", "n6"], await Unread(inbox));
    }

    [Fact]
    public async Task One_type_alone_and_its_counts_follow()
    {
        var inbox = await SeedInbox();

        Assert.Equal(new Answer(Marked: 1, UnreadCount: 5, TypesUnreadCount: 0), await MarkRead(inbox.User, "?types=GiftReceived"));
        Assert.Equal(["n1", "n3", "n6", "n7", "n8"], await Unread(inbox));
        Assert.Equal(3, await UnreadCount(inbox.User, $"?types={OnMyNovels}"));

        // A type with nothing unread marks nothing; the social ones are marked only when named.
        Assert.Equal(new Answer(Marked: 0, UnreadCount: 5, TypesUnreadCount: 0), await MarkRead(inbox.User, "?types=ReplyToComment"));
        Assert.Equal(new Answer(Marked: 2, UnreadCount: 3, TypesUnreadCount: 0), await MarkRead(inbox.User, "?types=NewFollower,LikeOnComment"));
        Assert.Equal(["n1", "n7", "n8"], await Unread(inbox));
    }

    [Fact]
    public async Task Another_members_notifications_are_never_touched()
    {
        var inbox = await SeedInbox();
        var other = await api.SignUp();
        await using (var db = api.Db())
        {
            db.Notifications.AddRange(
                Notice(other, inbox.User, NotificationType.CommentOnChapter, false, DateTime.UtcNow),
                Notice(other, inbox.User, NotificationType.GiftReceived, false, DateTime.UtcNow));
            await db.SaveChangesAsync();
        }

        Assert.Equal(4, (await MarkRead(inbox.User, $"?types={OnMyNovels}")).Marked);

        Assert.Equal(2, await UnreadCount(other));
        Assert.Equal(2, await UnreadCount(other, $"?types={OnMyNovels}"));
    }

    [Fact]
    public async Task Unknown_names_are_ignored_and_naming_no_known_type_marks_nothing()
    {
        var inbox = await SeedInbox();

        // The known one of the two: n1 (n5, also a comment, was read already).
        Assert.Equal(new Answer(Marked: 1, UnreadCount: 5, TypesUnreadCount: 0), await MarkRead(inbox.User, "?types=CommentOnChapter,NotAType"));
        Assert.Equal(["n3", "n4", "n6", "n7", "n8"], await Unread(inbox));

        // Unlike a list, where they mean every type, these mark nothing: those types don't exist here.
        foreach (var types in new[] { "NotAType", "NotAType,AnotherOne", Uri.EscapeDataString("Comment On Chapter") })
        {
            Assert.Equal(new Answer(Marked: 0, UnreadCount: 5, TypesUnreadCount: 0), await MarkRead(inbox.User, $"?types={types}"));
        }
        Assert.Equal(["n3", "n4", "n6", "n7", "n8"], await Unread(inbox));
    }

    [Fact]
    public async Task Names_match_in_any_letter_case_with_spaces_around_them_and_the_parameter_may_repeat()
    {
        var inbox = await SeedInbox();

        Assert.Equal(new Answer(Marked: 2, UnreadCount: 4, TypesUnreadCount: 0),
            await MarkRead(inbox.User, $"?types={Uri.EscapeDataString(" giftreceived , PRIVILEGESUBSCRIBED ")}"));
        Assert.Equal(["n1", "n3", "n6", "n8"], await Unread(inbox));

        Assert.Equal(new Answer(Marked: 2, UnreadCount: 2, TypesUnreadCount: 0),
            await MarkRead(inbox.User, "?types=reviewOnNovel&types=COMMENTONCHAPTER"));
        Assert.Equal(["n3", "n6"], await Unread(inbox));
    }

    [Fact]
    public async Task Types_that_name_nothing_are_refused_and_mark_nothing()
    {
        var inbox = await SeedInbox();

        foreach (var query in new[] { "", "?types=", $"?types={Uri.EscapeDataString(" , ,")}", "?types=&types=" })
        {
            var error = await (await api.Send(HttpMethod.Patch, $"/api/notifications/read{query}", inbox.User)).Error(HttpStatusCode.BadRequest);
            Assert.Equal("ValidationFailed", error.GetProperty("code").GetString());
            Assert.Equal(MarkTypesAsReadCommand.TypesRequiredMessage, error.GetProperty("message").GetString());
        }

        Assert.Equal(["n1", "n3", "n4", "n6", "n7", "n8"], await Unread(inbox));
    }

    [Fact]
    public async Task Read_all_still_marks_every_type()
    {
        var inbox = await SeedInbox();

        Assert.Equal(HttpStatusCode.NoContent, (await api.Send(HttpMethod.Patch, "/api/notifications/read-all", inbox.User)).StatusCode);

        Assert.Empty(await Unread(inbox));
        Assert.Equal(new Answer(Marked: 0, UnreadCount: 0, TypesUnreadCount: 0), await MarkRead(inbox.User, $"?types={OnMyNovels}"));
    }

    [Fact]
    public async Task Signed_out_it_is_401()
    {
        var response = await api.Send(HttpMethod.Patch, $"/api/notifications/read?types={OnMyNovels}");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
