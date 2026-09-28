using System.Net.Http.Json;
using System.Text.Json;
using Domain.Constants;
using Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Sareed_novels_backend.Tests.Integration;

/// <summary>
/// GET /api/notifications carries the parts each message names (#25): the novel's title, the chapter, the list's name,
/// the gift and how many. The app used to read them out of the Arabic sentence.
/// </summary>
[Collection(ReaderApiCollection.Name)]
public class NotificationPartsHttpTests(SardApiFactory api)
{
    private async Task<Dictionary<string, JsonElement>> NotificationsByType(ApiUser user, int expected)
    {
        for (var waited = 0; ; waited++)
        {
            var page = await (await api.Get("/api/notifications?pageSize=50", user)).OkJson();
            var items = page.GetProperty("notifications").EnumerateArray().ToList();
            if (items.Count >= expected || waited == 150)
            {
                Assert.Equal(expected, items.Count);
                return items.ToDictionary(n => n.GetProperty("type").GetString()!);
            }
            await Task.Delay(100); // the notifications are written in the background
        }
    }

    private static string? Text(JsonElement notification, string property) => notification.GetProperty(property).GetString();

    private static Guid? Id(JsonElement notification, string property) =>
        notification.GetProperty(property).ValueKind == JsonValueKind.Null ? null : notification.GetProperty(property).GetGuid();

    [Fact]
    public async Task Notifications_name_their_novel_chapter_list_and_gift_and_keep_up_with_renames()
    {
        var (author, reader) = (await api.SignUp(), await api.SignUp());
        var novel = await api.AddNovel(author, title: "ظل الأمير " + Seed.Marker());
        var (first, _) = await api.AddChapter(novel, "<p>فقرة</p>");
        (await api.Send(HttpMethod.Post, $"/api/library/track-progress/{first.Id}", reader)).EnsureSuccessStatusCode(); // in their library
        var lantern = new Gift { Id = Guid.NewGuid(), Name = "Lantern", NameAr = "فانوس", ImageUrl = "https://files.test/lantern.png", Cost = 10 };
        await using (var db = api.Db())
        {
            db.Gifts.Add(lantern);
            db.UserWallets.Add(new UserWallet { Id = Guid.NewGuid(), UserId = reader.Id, CurrentBalance = 1000 });
            await db.SaveChangesAsync();
        }

        // To the reader: a new chapter, and the author's like of their review. To the author: a comment on that
        // chapter, the review, a follow of their list and a gift.
        var chapterTitle = "الفصل الثاني " + Seed.Marker();
        var chapterId = (await (await api.Send(HttpMethod.Post, $"/api/novel/{novel.Id}/chapter", author,
            JsonContent.Create(new { status = "Published", title = chapterTitle, content = "<p>نص</p>" }))).OkJson()).GetProperty("id").GetGuid();
        await api.Comment(reader, $"/api/comment/chapter/{chapterId}");
        var review = await api.Review(reader, novel.Id);
        (await api.Send(HttpMethod.Post, $"/api/{novel.Id}/reviews/{review}/like", author)).EnsureSuccessStatusCode();
        var list = await api.ReadingList(author, name: "مفضلتي");
        (await api.Send(HttpMethod.Post, $"/api/readinglist/{list}/follow", reader)).EnsureSuccessStatusCode();
        (await api.Send(HttpMethod.Post, "/api/gift/send", reader, JsonContent.Create(new { giftId = lantern.Id, novelId = novel.Id, count = 3 })))
            .EnsureSuccessStatusCode();
        string listName;
        await using (var db = api.Db())
        {
            listName = await db.ReadingLists.Where(l => l.Id == list).Select(l => l.Name).SingleAsync();
        }

        var mine = await NotificationsByType(author, expected: 4);
        var comment = mine[NotificationType.CommentOnChapter];
        Assert.Equal(chapterId, Id(comment, "chapterId"));
        Assert.Equal(chapterTitle, Text(comment, "chapterTitle"));
        Assert.Contains(chapterTitle, Text(comment, "message"));
        Assert.Null(Id(comment, "novelId")); // as before: comment notifications get their novel from the comment's context
        var reviewed = mine[NotificationType.ReviewOnNovel];
        Assert.Equal((novel.Id, novel.Title), (Id(reviewed, "novelId"), Text(reviewed, "novelTitle")));
        Assert.Null(Id(reviewed, "chapterId"));
        var followed = mine[NotificationType.ReadingListFollowed];
        Assert.Equal((list, listName), (Id(followed, "relatedEntityId"), Text(followed, "readingListName")));
        var gift = mine[NotificationType.GiftReceived];
        Assert.Equal($"{reader.UserName} أرسل فانوس ×3 إلى روايتك «{novel.Title}»", Text(gift, "message"));
        Assert.Equal((lantern.Id, "فانوس", 3), (Id(gift, "giftId"), Text(gift, "giftNameAr"), gift.GetProperty("giftCount").GetInt32()));
        Assert.Equal((novel.Id, novel.Title, novel.Slug), (Id(gift, "novelId"), Text(gift, "novelTitle"), Text(gift, "novelSlug")));

        var theirs = await NotificationsByType(reader, expected: 2);
        var newChapter = theirs[NotificationType.NewChapterInLibrary];
        Assert.Equal((novel.Id, novel.Title), (Id(newChapter, "novelId"), Text(newChapter, "novelTitle")));
        Assert.Equal((chapterId, chapterTitle), (Id(newChapter, "chapterId"), Text(newChapter, "chapterTitle")));
        Assert.Equal((novel.Id, novel.Title), (Id(theirs[NotificationType.LikeOnReview], "novelId"), Text(theirs[NotificationType.LikeOnReview], "novelTitle")));
        // Parts a message doesn't name stay null.
        foreach (var notification in mine.Values.Concat(theirs.Values).Where(n => Text(n, "type") != NotificationType.GiftReceived))
        {
            Assert.Equal(JsonValueKind.Null, notification.GetProperty("giftId").ValueKind);
            Assert.Equal(JsonValueKind.Null, notification.GetProperty("giftNameAr").ValueKind);
            Assert.Equal(JsonValueKind.Null, notification.GetProperty("giftCount").ValueKind);
        }
        Assert.All(mine.Values.Concat(theirs.Values).Where(n => Text(n, "type") != NotificationType.ReadingListFollowed),
            n => Assert.Equal(JsonValueKind.Null, n.GetProperty("readingListName").ValueKind));

        // Renamed since, retired and deleted: the names are current (the message keeps the words it was sent with),
        // a retired gift is still named, and a deleted list has no name any more.
        await using (var db = api.Db())
        {
            await db.Novels.Where(n => n.Id == novel.Id).ExecuteUpdateAsync(s => s.SetProperty(n => n.Title, "الاسم الجديد"));
            await db.Chapters.Where(c => c.Id == chapterId).ExecuteUpdateAsync(s => s.SetProperty(c => c.Title, "عنوان جديد"));
            await db.Gifts.Where(g => g.Id == lantern.Id).ExecuteUpdateAsync(s => s.SetProperty(g => g.IsActive, false));
        }
        (await api.Send(HttpMethod.Delete, $"/api/readinglist/{list}", author)).EnsureSuccessStatusCode();

        mine = await NotificationsByType(author, expected: 4);
        Assert.Equal("عنوان جديد", Text(mine[NotificationType.CommentOnChapter], "chapterTitle"));
        Assert.Equal("الاسم الجديد", Text(mine[NotificationType.ReviewOnNovel], "novelTitle"));
        Assert.Contains(novel.Title, Text(mine[NotificationType.ReviewOnNovel], "message"));
        Assert.Equal("فانوس", Text(mine[NotificationType.GiftReceived], "giftNameAr"));
        Assert.Equal(JsonValueKind.Null, mine[NotificationType.ReadingListFollowed].GetProperty("readingListName").ValueKind);
        theirs = await NotificationsByType(reader, expected: 2);
        Assert.Equal(("الاسم الجديد", "عنوان جديد"),
            (Text(theirs[NotificationType.NewChapterInLibrary], "novelTitle"), Text(theirs[NotificationType.NewChapterInLibrary], "chapterTitle")));
    }

    [Theory]
    [InlineData("/novel/abcde-رواية/chapter/5f1c2d3e-4b5a-6978-8091-a2b3c4d5e6f7", "5f1c2d3e-4b5a-6978-8091-a2b3c4d5e6f7")]
    [InlineData("/novel/abcde-رواية/chapter/1", null)]
    [InlineData("/profile/noor", null)]
    [InlineData("/notifications", null)]
    public void A_comment_notifications_chapter_is_read_from_its_link(string actionUrl, string? chapterId) =>
        Assert.Equal(chapterId is null ? null : Guid.Parse(chapterId),
            Application.Notifications.Queries.GetNotifications.GetNotificationsQueryHandler.ChapterIdIn(actionUrl));
}
