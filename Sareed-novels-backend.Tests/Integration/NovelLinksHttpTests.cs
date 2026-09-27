using System.Text.Json;
using Domain.Constants;
using Domain.Entities;
using Domain.Seo;
using Microsoft.EntityFrameworkCore;

namespace Sareed_novels_backend.Tests.Integration;

/// <summary>
/// Lists that point at a novel carry its id and current slug, so the apps open it by id (or by the fresh slug) instead
/// of by a slug a rename made stale.
/// </summary>
[Collection(ReaderApiCollection.Name)]
public class NovelLinksHttpTests(SardApiFactory api)
{
    /// <summary>A notification row as NotificationService writes it.</summary>
    private static Notification Notice(ApiUser to, string type, string actorId, Guid? relatedId, string? relatedType, string actionUrl) => new()
    {
        Id = Guid.NewGuid(),
        UserId = to.Id,
        Type = type,
        ActorId = actorId,
        ActorDisplayName = "someone",
        Message = type,
        ActionUrl = actionUrl,
        RelatedEntityId = relatedId,
        RelatedEntityType = relatedType,
        CreatedAt = DateTime.UtcNow
    };

    private static Review Review(ApiUser reviewer, Novel novel) => new()
    {
        Id = Guid.NewGuid(),
        ReviewerId = reviewer.Id,
        NovelId = novel.Id,
        WritingQualityScore = 4,
        UpdatingStabilityScore = 4,
        CharacterDevelopmentScore = 4,
        WorldBuildingScore = 4,
        TotalAverageScore = 4,
        CreatedAt = DateTime.UtcNow
    };

    [Fact]
    public async Task Notifications_about_a_novel_carry_its_id_and_current_slug()
    {
        var me = await api.SignUp();
        var other = await api.SignUp();
        var novel = await api.AddNovel(me);
        var gone = await api.AddNovel(me);
        var review = Review(other, novel);
        var oldUrl = $"/novel/{novel.Slug}";
        var newSlug = Slugs.For(novel.Id, "اسم جديد");

        // Each notification with the novel id and slug it should list with (the slug after the rename below).
        var expected = new Dictionary<Guid, (Guid? NovelId, string? Slug)>();
        var notices = new List<Notification>();
        void Add(Notification notice, Guid? novelId, string? slug)
        {
            notices.Add(notice);
            expected[notice.Id] = (novelId, slug);
        }

        Add(Notice(me, NotificationType.GiftReceived, other.Id, novel.Id, "Gift", oldUrl), novel.Id, newSlug);
        Add(Notice(me, NotificationType.PrivilegeSubscribed, other.Id, novel.Id, "Privilege", oldUrl), novel.Id, newSlug);
        Add(Notice(me, NotificationType.NewChapterInLibrary, novel.Id.ToString(), Guid.NewGuid(), "Chapter", $"{oldUrl}/chapter/1"), novel.Id, newSlug);
        Add(Notice(me, NotificationType.ReviewOnNovel, other.Id, review.Id, "Review", oldUrl), novel.Id, newSlug);
        Add(Notice(me, NotificationType.LikeOnReview, other.Id, review.Id, "Review", oldUrl), novel.Id, newSlug);
        Add(Notice(me, NotificationType.GiftReceived, other.Id, gone.Id, "Gift", $"/novel/{gone.Slug}"), gone.Id, null);
        Add(Notice(me, NotificationType.ReviewOnNovel, other.Id, Guid.NewGuid(), "Review", oldUrl), null, null); // review deleted
        Add(Notice(me, NotificationType.CommentOnChapter, other.Id, Guid.NewGuid(), "Comment", $"{oldUrl}/chapter/1"), null, null);
        Add(Notice(me, NotificationType.NewFollower, other.Id, null, null, $"/profile/{other.UserName}"), null, null);

        await using (var db = api.Db())
        {
            db.Reviews.Add(review);
            db.Notifications.AddRange(notices);
            await db.SaveChangesAsync();
            // After the notifications were sent, one novel is renamed (a new slug) and the other deleted.
            await db.Novels.Where(n => n.Id == novel.Id).ExecuteUpdateAsync(s => s.SetProperty(n => n.Slug, newSlug));
            await db.Novels.Where(n => n.Id == gone.Id).ExecuteUpdateAsync(s => s.SetProperty(n => n.IsDeleted, true));
        }

        var page = await (await api.Get("/api/notifications?pageSize=50", me)).OkJson();

        var listed = page.GetProperty("notifications").EnumerateArray().ToList();
        Assert.Equal(notices.Count, listed.Count);
        foreach (var notification in listed)
        {
            var (novelId, slug) = expected[notification.GetProperty("id").GetGuid()];
            var type = notification.GetProperty("type").GetString();
            Assert.True(novelId == NullableGuid(notification.GetProperty("novelId")), $"novelId of {type}");
            Assert.True(slug == notification.GetProperty("novelSlug").GetString(), $"novelSlug of {type}");
        }
    }

    [Fact]
    public async Task Subscriptions_and_gift_history_carry_the_novel_slug()
    {
        var reader = await api.SignUp();
        var author = await api.SignUp();
        var novel = await api.AddNovel(author);
        var gift = new Gift { Id = Guid.NewGuid(), Name = "وردة", ImageUrl = "https://files.test/rose.png", Cost = 5 };
        await using (var db = api.Db())
        {
            db.Gifts.Add(gift);
            db.GiftTransactions.Add(new GiftTransaction
            {
                Id = Guid.NewGuid(), GiftId = gift.Id, NovelId = novel.Id, SenderId = reader.Id, Count = 2, TotalCost = 10
            });
            db.NovelPrivilegeSubscriptions.Add(new NovelPrivilegeSubscription
            {
                Id = Guid.NewGuid(), NovelId = novel.Id, UserId = reader.Id, AmountPaid = 50, IsActive = true
            });
            await db.SaveChangesAsync();
        }

        var subscriptions = await (await api.Get("/api/privilege/my-subscriptions", reader)).OkJson();
        var subscription = Assert.Single(subscriptions.GetProperty("subscriptions").EnumerateArray());
        Assert.Equal(novel.Id, subscription.GetProperty("novelId").GetGuid());
        Assert.Equal(novel.Slug, subscription.GetProperty("novelSlug").GetString());

        var history = await (await api.Get("/api/gift/my-history", reader)).OkJson();
        var sent = Assert.Single(history.GetProperty("items").EnumerateArray());
        Assert.Equal(novel.Id, sent.GetProperty("novelId").GetGuid());
        Assert.Equal(novel.Slug, sent.GetProperty("novelSlug").GetString());

        var novelGifts = await (await api.Get($"/api/gift/novel/{novel.Id}")).OkJson();
        var received = Assert.Single(novelGifts.GetProperty("items").EnumerateArray());
        Assert.Equal(novel.Id, received.GetProperty("novelId").GetGuid());
        Assert.Equal(novel.Slug, received.GetProperty("novelSlug").GetString());
        Assert.Equal(reader.UserName, received.GetProperty("senderUserName").GetString());
    }

    private static Guid? NullableGuid(JsonElement value) => value.ValueKind == JsonValueKind.Null ? null : value.GetGuid();
}
