using Domain.Constants;
using Domain.Entities;
using Infrastructure.Push;

namespace Sareed_novels_backend.Tests.Unit;

public class PushMessagesTests
{
    public static readonly TheoryData<string, string, string> Types = new()
    {
        { NotificationType.NewFollower, NotificationGroups.Social, "متابع جديد" },
        { NotificationType.CommentOnChapter, NotificationGroups.Social, "تعليق جديد" },
        { NotificationType.CommentOnPost, NotificationGroups.Social, "تعليق جديد" },
        { NotificationType.ReplyToComment, NotificationGroups.Social, "ردّ جديد على تعليقك" },
        { NotificationType.NewChapterInLibrary, NotificationGroups.Chapters, "فصل جديد" },
        { NotificationType.ReviewOnNovel, NotificationGroups.Social, "تقييم جديد لروايتك" },
        { NotificationType.LikeOnPost, NotificationGroups.Social, "إعجاب جديد" },
        { NotificationType.LikeOnComment, NotificationGroups.Social, "إعجاب جديد" },
        { NotificationType.LikeOnReview, NotificationGroups.Social, "إعجاب جديد" },
        { NotificationType.ReadingListFollowed, NotificationGroups.Social, "متابع جديد لقائمتك" },
        { NotificationType.GiftReceived, NotificationGroups.Support, "هدية جديدة" },
        { NotificationType.PrivilegeSubscribed, NotificationGroups.Support, "اشتراك جديد" },
        { "SomeFutureType", NotificationGroups.Social, "إشعار جديد" }
    };

    private static Notification Notification(string type, Guid? relatedEntityId = null) => new()
    {
        Id = Guid.NewGuid(),
        UserId = "recipient",
        Type = type,
        ActorId = "actor",
        ActorDisplayName = "سارة",
        Message = "سارة أعجبت بتعليقك",
        ActionUrl = "/notifications",
        RelatedEntityId = relatedEntityId,
        CreatedAt = DateTime.UtcNow
    };

    [Theory]
    [MemberData(nameof(Types))]
    public void Every_type_has_an_arabic_title_and_goes_to_its_groups_channel(string type, string channel, string title)
    {
        var message = PushMessages.Build(Notification(type, Guid.NewGuid()), new PushTarget(), 3, "device-token");

        Assert.Equal(title, message.Title);
        Assert.Equal(channel, message.ChannelId);
        Assert.Equal("سارة أعجبت بتعليقك", message.Body);
        Assert.Equal("device-token", message.Token);
        Assert.Equal(3, message.UnreadCount);
    }

    [Fact]
    public void The_data_carries_every_key_as_a_string_empty_when_it_does_not_apply()
    {
        var comment = Guid.NewGuid();
        var post = Guid.NewGuid();
        var target = new PushTarget { ActorUserName = "sara", CommentId = comment, PostId = post, PostAuthorUserName = "noor" };

        var data = PushMessages.DataFor(Notification(NotificationType.CommentOnPost, comment), target, 7);

        Assert.Equal(PushMessages.DataKeys.Order(), data.Keys.Order());
        Assert.Equal(comment.ToString(), data["commentId"]);
        Assert.Equal(post.ToString(), data["postId"]);
        Assert.Equal("noor", data["postAuthorUserName"]);
        Assert.Equal(comment.ToString(), data["relatedEntityId"]);
        Assert.Equal("sara", data["actorUserName"]);
        Assert.Equal("7", data["unreadCount"]);
        Assert.Equal("", data["novelId"]);
        Assert.Equal("", data["chapterId"]);
        Assert.Equal("", data["relatedEntityType"]);
    }

    [Fact]
    public void A_notification_about_no_post_carries_an_empty_post_author()
    {
        var data = PushMessages.DataFor(Notification(NotificationType.NewFollower), new PushTarget { ActorUserName = "sara" }, 1);

        Assert.Contains("postAuthorUserName", PushMessages.DataKeys);
        Assert.Equal("", data["postAuthorUserName"]);
        Assert.Equal("", data["postId"]);
        Assert.All(data.Values, Assert.NotNull);
    }

    [Fact]
    public void Likes_on_one_item_share_a_collapse_key_and_likes_on_different_items_do_not()
    {
        var comment = Guid.NewGuid();
        var first = PushMessages.CollapseKeyFor(Notification(NotificationType.LikeOnComment, comment), new PushTarget());
        var second = PushMessages.CollapseKeyFor(Notification(NotificationType.LikeOnComment, comment), new PushTarget());
        var other = PushMessages.CollapseKeyFor(Notification(NotificationType.LikeOnComment, Guid.NewGuid()), new PushTarget());

        Assert.Equal($"LikeOnComment:{comment}", first);
        Assert.Equal(first, second);
        Assert.NotEqual(first, other);
    }

    [Fact]
    public void Replies_collapse_per_thread_and_new_followers_into_one()
    {
        var thread = Guid.NewGuid();
        var reply = PushMessages.CollapseKeyFor(Notification(NotificationType.ReplyToComment, Guid.NewGuid()),
            new PushTarget { CommentId = Guid.NewGuid(), ParentCommentId = thread });

        Assert.Equal($"ReplyToComment:{thread}", reply);
        Assert.Equal("NewFollower", PushMessages.CollapseKeyFor(Notification(NotificationType.NewFollower), new PushTarget()));
    }

    [Fact]
    public void A_notification_it_cannot_place_does_not_collapse_with_others_and_keys_fit_apns()
    {
        var unplaced = Notification("SomeFutureTypeWithAVeryLongNameThatKeepsGoingAndGoing");

        var key = PushMessages.CollapseKeyFor(unplaced, new PushTarget());

        Assert.EndsWith(unplaced.Id.ToString(), key);
        Assert.True(key.Length <= 64);
    }
}
