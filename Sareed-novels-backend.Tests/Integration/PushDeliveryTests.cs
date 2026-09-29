using System.Net;
using Domain.Constants;
using Domain.Entities;
using Infrastructure.BackgroundJobs;
using Infrastructure.Persistence;
using Infrastructure.Push;
using Infrastructure.Repositories;
using Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Sareed_novels_backend.Tests.Integration;

/// <summary>
/// The outbox worker end to end on SQL Server, with FCM replaced by <see cref="FakeFcm"/>: notifications are created
/// the way the app creates them (NotificationService), then <see cref="PushOutboxProcessor"/> drains the outbox.
/// </summary>
public class PushDeliveryTests(SqlServerDatabase database) : IClassFixture<SqlServerDatabase>, IAsyncLifetime
{
    private readonly FakeFcm fcm = new();

    // Tests in a class share the database: start each one with an empty outbox.
    public async Task InitializeAsync()
    {
        await using var db = database.CreateContext();
        await db.PushOutbox.ExecuteDeleteAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private sealed record World(User Recipient, User Actor, List<UserDevice> Devices);

    private async Task<World> SeedWorld(int devices = 1)
    {
        await using var db = database.CreateContext();
        var (recipient, actor) = (Seed.User(), Seed.User());
        var list = Enumerable.Range(0, devices).Select(i => new UserDevice
        {
            Id = Guid.NewGuid(),
            UserId = recipient.Id,
            Token = $"token-{i}-{Guid.NewGuid():N}",
            Platform = DevicePlatforms.Android,
            CreatedAt = DateTime.UtcNow,
            LastSeenAt = DateTime.UtcNow
        }).ToList();
        db.Users.AddRange(recipient, actor);
        db.UserDevices.AddRange(list);
        await db.SaveChangesAsync();
        return new World(recipient, actor, list);
    }

    private async Task Notify(Func<NotificationService, Task> send)
    {
        await using var db = database.CreateContext();
        var logger = new ListLogger<NotificationService>();
        await send(new NotificationService(logger, new NotificationsRepository(db)));
        Assert.DoesNotContain(logger.Entries, e => e.Level >= LogLevel.Error);
    }

    private Task Followed(World world) => Notify(s => s.SendNewFollowerNotification(world.Recipient.Id, world.Actor));

    private async Task<int> ProcessOnce(PushDeliveryOptions? options = null, TimeProvider? clock = null, ListLogger<PushOutboxProcessor>? logger = null)
    {
        await using var db = database.CreateContext();
        return await PushTesting.Processor(db, fcm, options, clock, logger).ProcessDueAsync(CancellationToken.None);
    }

    private async Task Drain(PushDeliveryOptions? options = null, TimeProvider? clock = null)
    {
        while (await ProcessOnce(options, clock) > 0)
        {
        }
    }

    private async Task<List<PushOutboxMessage>> Outbox()
    {
        await using var db = database.CreateContext();
        return await db.PushOutbox.AsNoTracking().ToListAsync();
    }

    private static MutableClock ClockAfterQueueing() => new(DateTime.UtcNow.AddSeconds(1));

    [Fact]
    public async Task Every_notification_type_is_pushed_with_its_arabic_title_its_message_and_the_ids_the_app_needs()
    {
        var world = await SeedWorld();
        var (me, actor) = (world.Recipient, world.Actor);

        // My novel with a chapter and a paragraph, a second novel I reviewed, my post and reading list.
        await using var db = database.CreateContext();
        var novel = Seed.Novel(me, "رواية " + Seed.Marker());
        var chapter = Seed.Chapters(novel, 1, DateTime.UtcNow.AddDays(-1))[0];
        var paragraph = new ChapterParagraph { Id = Guid.NewGuid(), ChapterId = chapter.Id, Content = "فقرة", ContentHash = "hash", OrderIndex = 0 };
        var otherNovel = Seed.Novel(actor, "رواية " + Seed.Marker());
        var post = new Post { Id = Guid.NewGuid(), UserId = me.Id, Content = "منشور" };
        Comments Comment(User by, Guid? chapterId = null, Guid? paragraphId = null, Guid? postId = null, Guid? parent = null) => new()
        {
            Id = Guid.NewGuid(), UserId = by.Id, Content = "تعليق", ChapterId = chapterId, ParagraphId = paragraphId, PostId = postId,
            ParentCommentId = parent
        };
        var onMyChapter = Comment(actor, chapterId: chapter.Id);
        var mine = Comment(me, chapterId: chapter.Id);
        var replyToMine = Comment(actor, chapterId: chapter.Id, parent: mine.Id);
        var mineOnParagraph = Comment(me, paragraphId: paragraph.Id);
        var onMyPost = Comment(actor, postId: post.Id);
        var reviewOfMyNovel = new Review { Id = Guid.NewGuid(), ReviewerId = actor.Id, NovelId = novel.Id, Content = "رائعة" };
        var myReview = new Review { Id = Guid.NewGuid(), ReviewerId = me.Id, NovelId = otherNovel.Id, Content = "جيدة" };
        var readingList = new ReadingList { Id = Guid.NewGuid(), UserId = me.Id, Name = "قائمتي" };
        var gift = new Gift { Id = Guid.NewGuid(), Name = "وردة", NameAr = "وردة", ImageUrl = "https://example.test/rose.png", Cost = 10 };
        db.AddRange(novel, chapter, paragraph, otherNovel, post, onMyChapter, mine, replyToMine, mineOnParagraph, onMyPost,
            reviewOfMyNovel, myReview, readingList, gift);
        await db.SaveChangesAsync();

        await Notify(s => s.SendNewFollowerNotification(me.Id, actor));
        await Notify(s => s.SendCommentOnChapterNotification(me.Id, actor, onMyChapter.Id, novel, chapter));
        await Notify(s => s.SendCommentOnPostNotification(me.Id, actor, onMyPost.Id, me.UserName!));
        await Notify(s => s.SendReplyToCommentNotification(me.Id, actor, replyToMine.Id, mine, novel, chapter));
        await Notify(s => s.SendNewChapterInLibraryNotification([me.Id], novel, chapter));
        await Notify(s => s.SendReviewOnNovelNotification(me.Id, actor, reviewOfMyNovel.Id, novel));
        await Notify(s => s.SendLikeOnPostNotification(me.Id, actor, post.Id, me.UserName!));
        await Notify(s => s.SendLikeOnCommentNotification(me.Id, actor, mineOnParagraph.Id, mineOnParagraph, novel, chapter));
        await Notify(s => s.SendLikeOnReviewNotification(me.Id, actor, myReview.Id, otherNovel));
        await Notify(s => s.SendReadingListFollowedNotification(me.Id, actor, readingList.Id, readingList.Name));
        await Notify(s => s.SendGiftReceivedNotification(me.Id, actor, novel, gift, 3, Guid.NewGuid()));
        await Notify(s => s.SendPrivilegeSubscribedNotification(me.Id, actor, novel));

        await Drain();

        var pushes = fcm.Requests.ToDictionary(p => p.Data["type"]);
        Assert.Equal(12, pushes.Count);
        await using var check = database.CreateContext();
        var notifications = await check.Notifications.AsNoTracking().Where(n => n.UserId == me.Id).ToDictionaryAsync(n => n.Type);
        foreach (var (type, push) in pushes)
        {
            var notification = notifications[type];
            Assert.Equal(world.Devices[0].Token, push.Token);
            Assert.Equal("Bearer " + FakeFcmTokens.Token, push.Authorization);
            Assert.Equal("https://fcm.googleapis.com/v1/projects/test-project/messages:send", push.Uri.ToString());
            Assert.Equal(PushMessages.TitleFor(type), push.Title);
            Assert.Equal(notification.Message, push.Text);
            Assert.Equal(PushMessages.DataKeys.Order(), push.Data.Keys.Order());
            Assert.Equal(notification.Id.ToString(), push.Data["notificationId"]);
            Assert.Equal("12", push.Data["unreadCount"]);
            Assert.Equal(notification.ActorId, push.Data["actorId"]);
        }

        string Id(Guid id) => id.ToString();
        void Expect(string type, string channel, string collapseKey, params (string Key, string Value)[] data)
        {
            var push = pushes[type];
            Assert.Equal(channel, push.ChannelId);
            Assert.Equal(collapseKey, push.CollapseKey);
            var expected = PushMessages.DataKeys
                .Except(["notificationId", "type", "relatedEntityId", "relatedEntityType", "actorId", "unreadCount"])
                .ToDictionary(k => k, k => "");
            foreach (var (key, value) in data)
            {
                expected[key] = value;
            }
            Assert.Equal(expected, push.Data.Where(d => expected.ContainsKey(d.Key)).ToDictionary());
        }

        var actorName = ("actorUserName", actor.UserName!);
        var novelIds = new[] { ("novelId", Id(novel.Id)), ("novelSlug", novel.Slug) };
        Expect(NotificationType.NewFollower, "social", "NewFollower", actorName);
        Expect(NotificationType.CommentOnChapter, "social", $"CommentOnChapter:{chapter.Id}",
            [actorName, ("commentId", Id(onMyChapter.Id)), ("chapterId", Id(chapter.Id)), .. novelIds]);
        var myPost = new[] { ("postId", Id(post.Id)), ("postAuthorUserName", me.UserName!) };
        Expect(NotificationType.CommentOnPost, "social", $"CommentOnPost:{post.Id}", [actorName, ("commentId", Id(onMyPost.Id)), .. myPost]);
        Expect(NotificationType.ReplyToComment, "social", $"ReplyToComment:{mine.Id}",
            [actorName, ("commentId", Id(replyToMine.Id)), ("parentCommentId", Id(mine.Id)), ("chapterId", Id(chapter.Id)), .. novelIds]);
        Expect(NotificationType.NewChapterInLibrary, "chapters", $"NewChapterInLibrary:{novel.Id}",
            [("chapterId", Id(chapter.Id)), .. novelIds]);
        Expect(NotificationType.ReviewOnNovel, "social", $"ReviewOnNovel:{novel.Id}",
            [actorName, ("reviewId", Id(reviewOfMyNovel.Id)), .. novelIds]);
        Expect(NotificationType.LikeOnPost, "social", $"LikeOnPost:{post.Id}", [actorName, .. myPost]);
        Expect(NotificationType.LikeOnComment, "social", $"LikeOnComment:{mineOnParagraph.Id}",
            [actorName, ("commentId", Id(mineOnParagraph.Id)), ("paragraphId", Id(paragraph.Id)), ("chapterId", Id(chapter.Id)), .. novelIds]);
        Expect(NotificationType.LikeOnReview, "social", $"LikeOnReview:{myReview.Id}",
            actorName, ("reviewId", Id(myReview.Id)), ("novelId", Id(otherNovel.Id)), ("novelSlug", otherNovel.Slug));
        Expect(NotificationType.ReadingListFollowed, "social", $"ReadingListFollowed:{readingList.Id}",
            actorName, ("readingListId", Id(readingList.Id)));
        Expect(NotificationType.GiftReceived, "support", $"GiftReceived:{novel.Id}", [actorName, .. novelIds]);
        Expect(NotificationType.PrivilegeSubscribed, "support", $"PrivilegeSubscribed:{novel.Id}", [actorName, .. novelIds]);

        Assert.All(await Outbox(), o => Assert.Equal(PushOutboxStatus.Sent, o.Status));
    }

    [Fact]
    public async Task A_reply_or_like_under_someone_elses_post_names_the_posts_author()
    {
        var world = await SeedWorld();
        var (me, actor) = (world.Recipient, world.Actor);

        // Noor's post; I commented on it, the actor replies to my comment and likes it.
        await using var db = database.CreateContext();
        var noor = Seed.User();
        var post = new Post { Id = Guid.NewGuid(), UserId = noor.Id, Content = "منشور" };
        var mine = new Comments { Id = Guid.NewGuid(), UserId = me.Id, Content = "تعليقي", PostId = post.Id };
        var reply = new Comments { Id = Guid.NewGuid(), UserId = actor.Id, Content = "ردّ", PostId = post.Id, ParentCommentId = mine.Id };
        db.AddRange(noor, post, mine, reply);
        await db.SaveChangesAsync();

        await Notify(s => s.SendReplyToCommentNotification(me.Id, actor, reply.Id, mine, postAuthorUsername: noor.UserName));
        await Notify(s => s.SendLikeOnCommentNotification(me.Id, actor, mine.Id, mine, postAuthorUsername: noor.UserName));
        await Drain();

        var pushes = fcm.Requests.ToDictionary(p => p.Data["type"]);
        var replied = pushes[NotificationType.ReplyToComment].Data;
        Assert.Equal(noor.UserName, replied["postAuthorUserName"]);
        Assert.Equal(post.Id.ToString(), replied["postId"]);
        Assert.Equal(reply.Id.ToString(), replied["commentId"]);
        Assert.Equal(mine.Id.ToString(), replied["parentCommentId"]);
        Assert.Equal(actor.UserName, replied["actorUserName"]);
        var liked = pushes[NotificationType.LikeOnComment].Data;
        Assert.Equal(noor.UserName, liked["postAuthorUserName"]);
        Assert.Equal(post.Id.ToString(), liked["postId"]);
        Assert.Equal(mine.Id.ToString(), liked["commentId"]);
        Assert.All(pushes.Values, p => Assert.Equal(PushMessages.DataKeys.Order(), p.Data.Keys.Order()));
    }

    [Fact]
    public async Task A_gift_push_quotes_the_senders_message_as_the_gift_record_has_it_when_sent()
    {
        var world = await SeedWorld();
        var (me, actor) = (world.Recipient, world.Actor);

        // My novel, and four roses from the actor: with a message, a long one, one a moderator removes before the push
        // goes, and none.
        await using var db = database.CreateContext();
        var novel = Seed.Novel(me, "رواية " + Seed.Marker());
        var rose = new Gift { Id = Guid.NewGuid(), Name = "Rose", NameAr = "وردة", ImageUrl = "https://example.test/rose.png", Cost = 10 };
        GiftTransaction Sent(string? message) => new()
        {
            Id = Guid.NewGuid(), GiftId = rose.Id, NovelId = novel.Id, SenderId = actor.Id, Count = 1, TotalCost = 10, Message = message
        };
        var (short_, long_, removed, none) = (Sent("شكراً على الفصل الأخير 🌹"), Sent(new string('ب', 150)), Sent("رسالة مخالفة"), Sent(null));
        db.AddRange(novel, rose, short_, long_, removed, none);
        await db.SaveChangesAsync();
        foreach (var gift in new[] { short_, long_, removed, none })
        {
            await Notify(s => s.SendGiftReceivedNotification(me.Id, actor, novel, rose, 1, gift.Id));
        }
        await db.GiftTransactions.Where(t => t.Id == removed.Id).ExecuteUpdateAsync(s => s.SetProperty(t => t.Message, (string?)null));

        await Drain();

        await using var check = database.CreateContext();
        var giftOf = await check.Notifications.AsNoTracking().Where(n => n.UserId == me.Id)
            .ToDictionaryAsync(n => n.Id.ToString(), n => n.GiftTransactionId);
        var bodies = fcm.Requests.ToDictionary(p => giftOf[p.Data["notificationId"]]!.Value, p => p);
        Assert.Equal(4, bodies.Count);
        var sentence = $"{actor.DisplayName} أرسل وردة إلى روايتك «{novel.Title}»";
        Assert.All(bodies.Values, p => Assert.Equal("هدية جديدة", p.Title));
        Assert.Equal($"{sentence}\n«شكراً على الفصل الأخير 🌹»", bodies[short_.Id].Text);
        Assert.Equal($"{sentence}\n«{new string('ب', 99)}…»", bodies[long_.Id].Text);
        Assert.Equal(sentence, bodies[removed.Id].Text);
        Assert.Equal(sentence, bodies[none.Id].Text);
        // The data are as before: the message isn't one of its keys.
        Assert.All(bodies.Values, p => Assert.Equal(PushMessages.DataKeys.Order(), p.Data.Keys.Order()));
    }

    [Fact]
    public async Task A_batch_claims_at_most_BatchSize_rows()
    {
        var world = await SeedWorld(devices: 7);
        await Followed(world);
        var options = new PushDeliveryOptions { BatchSize = 3 };

        Assert.Equal(3, await ProcessOnce(options));
        Assert.Equal(3, fcm.Requests.Count);
        Assert.Equal(3, await ProcessOnce(options));
        Assert.Equal(1, await ProcessOnce(options));
        Assert.Equal(0, await ProcessOnce(options));
        Assert.Equal(7, fcm.Requests.Select(r => r.Token).Distinct().Count());
    }

    [Fact]
    public async Task Sends_run_in_parallel_but_never_more_than_MaxParallelSends_at_once()
    {
        var world = await SeedWorld(devices: 20);
        await Followed(world);
        fcm.Delay = TimeSpan.FromMilliseconds(60);

        await ProcessOnce(new PushDeliveryOptions { BatchSize = 20, MaxParallelSends = 3 });

        Assert.Equal(20, fcm.Requests.Count);
        Assert.InRange(fcm.MaxInFlight, 2, 3);
    }

    [Fact]
    public async Task A_temporary_failure_is_retried_with_exponential_backoff_and_retry_after_is_respected()
    {
        var world = await SeedWorld();
        await Followed(world);
        var clock = ClockAfterQueueing();
        var options = new PushDeliveryOptions { BaseRetryDelay = TimeSpan.FromSeconds(30) };

        fcm.Respond = _ => FakeFcm.Unavailable(retryAfter: TimeSpan.FromSeconds(120));
        await ProcessOnce(options, clock);
        var row = Assert.Single(await Outbox());
        Assert.Equal((PushOutboxStatus.Pending, 1), (row.Status, row.Attempts));
        Assert.InRange(row.NextAttemptAt, clock.UtcNow.AddSeconds(120), clock.UtcNow.AddSeconds(121));
        Assert.Contains("UNAVAILABLE", row.LastError);

        Assert.Equal(0, await ProcessOnce(options, clock)); // not due yet

        clock.Advance(TimeSpan.FromSeconds(121));
        fcm.Respond = _ => FakeFcm.Error(HttpStatusCode.InternalServerError, "INTERNAL", "Internal error", "INTERNAL");
        await ProcessOnce(options, clock);
        row = Assert.Single(await Outbox());
        Assert.Equal((PushOutboxStatus.Pending, 2), (row.Status, row.Attempts));
        // Second attempt: the base delay doubled, plus up to 20% jitter.
        Assert.InRange(row.NextAttemptAt, clock.UtcNow.AddSeconds(60), clock.UtcNow.AddSeconds(72));

        clock.Advance(TimeSpan.FromSeconds(73));
        fcm.Respond = _ => FakeFcm.Ok();
        await ProcessOnce(options, clock);
        row = Assert.Single(await Outbox());
        Assert.Equal((PushOutboxStatus.Sent, 3), (row.Status, row.Attempts));
        Assert.Null(row.LastError);
        Assert.Equal(3, fcm.Requests.Count);
    }

    [Fact]
    public async Task Rate_limiting_waits_at_least_a_minute()
    {
        var world = await SeedWorld();
        await Followed(world);
        var clock = ClockAfterQueueing();
        fcm.Respond = _ => FakeFcm.Error(HttpStatusCode.TooManyRequests, "RESOURCE_EXHAUSTED", "Quota exceeded", "QUOTA_EXCEEDED");

        await ProcessOnce(new PushDeliveryOptions { BaseRetryDelay = TimeSpan.FromSeconds(5) }, clock);

        var row = Assert.Single(await Outbox());
        Assert.Equal(PushOutboxStatus.Pending, row.Status);
        Assert.True(row.NextAttemptAt >= clock.UtcNow.AddMinutes(1));
    }

    [Fact]
    public async Task A_push_that_keeps_failing_is_given_up_after_MaxAttempts_with_a_warning_and_the_notification_stays()
    {
        var world = await SeedWorld();
        await Followed(world);
        var clock = ClockAfterQueueing();
        var options = new PushDeliveryOptions { MaxAttempts = 3, BaseRetryDelay = TimeSpan.FromSeconds(1), MaxRetryDelay = TimeSpan.FromSeconds(2) };
        var logger = new ListLogger<PushOutboxProcessor>();
        fcm.Respond = _ => FakeFcm.Unavailable();

        for (var i = 0; i < 5; i++)
        {
            await ProcessOnce(options, clock, logger);
            clock.Advance(TimeSpan.FromSeconds(5));
        }

        Assert.Equal(3, fcm.Requests.Count);
        var row = Assert.Single(await Outbox());
        Assert.Equal((PushOutboxStatus.Failed, 3), (row.Status, row.Attempts));
        Assert.StartsWith("gave up after 3 attempts", row.LastError);
        Assert.Contains(logger.Warnings, w => w.Contains("won't be retried") && w.Contains("gave up after 3 attempts") && w.Contains("UNAVAILABLE"));
        Assert.Contains(logger.Warnings, w => w.Contains("will be retried"));

        await using var db = database.CreateContext();
        var notification = await db.Notifications.SingleAsync(n => n.UserId == world.Recipient.Id);
        Assert.False(notification.IsRead);
    }

    [Theory]
    [InlineData("unregistered", true)]
    [InlineData("invalid-token", true)]
    [InlineData("invalid-token-field", true)]
    [InlineData("invalid-other-field", false)]
    [InlineData("sender-id-mismatch", false)]
    public async Task Tokens_fcm_calls_invalid_are_removed_and_nothing_else_is(string answer, bool removed)
    {
        var world = await SeedWorld(devices: 2);
        var (dead, healthy) = (world.Devices[0], world.Devices[1]);
        await Followed(world);
        var logger = new ListLogger<PushOutboxProcessor>();
        fcm.Respond = push => push.Token != dead.Token ? FakeFcm.Ok() : answer switch
        {
            "unregistered" => FakeFcm.Unregistered(),
            "invalid-token" => FakeFcm.InvalidToken(),
            "invalid-token-field" => FakeFcm.Error(HttpStatusCode.BadRequest, "INVALID_ARGUMENT", "Invalid value", "INVALID_ARGUMENT", invalidField: "message.token"),
            "invalid-other-field" => FakeFcm.Error(HttpStatusCode.BadRequest, "INVALID_ARGUMENT", "Invalid value at 'message.data'", "INVALID_ARGUMENT", invalidField: "message.data"),
            _ => FakeFcm.Error(HttpStatusCode.Forbidden, "PERMISSION_DENIED", "SenderId mismatch", "SENDER_ID_MISMATCH")
        };

        await ProcessOnce(logger: logger);

        await using var db = database.CreateContext();
        var devices = await db.UserDevices.Where(d => d.UserId == world.Recipient.Id).Select(d => d.Id).ToListAsync();
        Assert.Equal(removed, !devices.Contains(dead.Id));
        Assert.Contains(healthy.Id, devices);

        var rows = (await Outbox()).ToDictionary(o => o.DeviceId);
        Assert.Equal(PushOutboxStatus.Sent, rows[healthy.Id].Status);
        Assert.Equal(PushOutboxStatus.Failed, rows[dead.Id].Status);
        if (removed)
        {
            Assert.StartsWith("invalid token, device removed", rows[dead.Id].LastError);
        }
        else
        {
            Assert.Contains(logger.Warnings, w => w.Contains("won't be retried"));
        }
        Assert.True(await db.Notifications.AnyAsync(n => n.UserId == world.Recipient.Id));
    }

    [Fact]
    public async Task A_switched_off_group_skips_the_push_but_keeps_the_in_app_notification()
    {
        var world = await SeedWorld();
        await using (var db = database.CreateContext())
        {
            await new NotificationPreferencesRepository(db).Update(world.Recipient.Id, social: null, chapters: false, support: null);
            var novel = Seed.Novel(world.Actor, "رواية " + Seed.Marker());
            var chapter = Seed.Chapters(novel, 1, DateTime.UtcNow)[0];
            db.AddRange(novel, chapter);
            await db.SaveChangesAsync();
            await Notify(s => s.SendNewChapterInLibraryNotification([world.Recipient.Id], novel, chapter));
        }
        await Followed(world);

        await Drain();

        Assert.Equal(NotificationType.NewFollower, Assert.Single(fcm.Requests).Data["type"]);
        var rows = await Outbox();
        Assert.Single(rows, r => r.Status == PushOutboxStatus.Skipped && r.LastError!.Contains("chapters"));
        await using var check = database.CreateContext();
        Assert.Equal(2, await check.Notifications.CountAsync(n => n.UserId == world.Recipient.Id));
    }

    [Fact]
    public async Task A_push_waiting_for_a_phone_now_signed_into_by_someone_else_is_not_sent_to_it()
    {
        var world = await SeedWorld();
        await Followed(world);
        await using (var db = database.CreateContext())
        {
            await new UserDevicesRepository(db).Upsert(new UserDevice
            {
                UserId = world.Actor.Id, Token = world.Devices[0].Token, Platform = "android", Locale = "ar",
                CreatedAt = DateTime.UtcNow, LastSeenAt = DateTime.UtcNow
            });
        }

        await Drain();

        Assert.Empty(fcm.Requests);
        Assert.Equal(PushOutboxStatus.Skipped, Assert.Single(await Outbox()).Status);
    }

    [Fact]
    public async Task A_push_still_waiting_after_MaxAge_is_skipped_as_stale()
    {
        var world = await SeedWorld();
        await Followed(world);
        var clock = new MutableClock(DateTime.UtcNow.AddHours(13));

        await Drain(new PushDeliveryOptions { MaxAge = TimeSpan.FromHours(12) }, clock);

        Assert.Empty(fcm.Requests);
        Assert.StartsWith("stale", Assert.Single(await Outbox()).LastError);
    }

    [Fact]
    public async Task A_claimed_push_is_not_claimed_again_until_its_lease_ends()
    {
        var world = await SeedWorld();
        await Followed(world);
        var clock = ClockAfterQueueing();
        var options = new PushDeliveryOptions { Lease = TimeSpan.FromMinutes(5) };

        // The first worker dies mid-send (an app-pool recycle): the row stays claimed.
        using (var cancel = new CancellationTokenSource())
        {
            fcm.Respond = _ =>
            {
                cancel.Cancel();
                throw new OperationCanceledException(cancel.Token);
            };
            await using var db = database.CreateContext();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
                PushTesting.Processor(db, fcm, options, clock).ProcessDueAsync(cancel.Token));
        }
        fcm.Respond = _ => FakeFcm.Ok();

        clock.Advance(TimeSpan.FromMinutes(4));
        Assert.Equal(0, await ProcessOnce(options, clock));

        clock.Advance(TimeSpan.FromMinutes(2));
        Assert.Equal(1, await ProcessOnce(options, clock));
        var row = Assert.Single(await Outbox());
        Assert.Equal((PushOutboxStatus.Sent, 2), (row.Status, row.Attempts));
    }

    [Fact]
    public async Task Two_workers_draining_at_once_send_each_push_once()
    {
        var world = await SeedWorld(devices: 30);
        await Followed(world);
        fcm.Delay = TimeSpan.FromMilliseconds(20);
        var options = new PushDeliveryOptions { BatchSize = 5 };

        await Task.WhenAll(Drain(options), Drain(options));

        Assert.Equal(30, fcm.Requests.Count);
        Assert.Equal(30, fcm.Requests.Select(r => r.Token).Distinct().Count());
    }

    [Fact]
    public async Task An_exception_from_the_sender_is_a_retry_not_a_lost_push()
    {
        var world = await SeedWorld();
        await Followed(world);
        fcm.Respond = _ => throw new InvalidOperationException("boom");

        await ProcessOnce(clock: ClockAfterQueueing());

        var row = Assert.Single(await Outbox());
        Assert.Equal(PushOutboxStatus.Pending, row.Status);
        Assert.Contains("boom", row.LastError);
    }

    [Fact]
    public async Task With_push_disabled_queued_pushes_are_marked_skipped_with_the_reason_and_nothing_is_sent()
    {
        var world = await SeedWorld(devices: 2);
        await Followed(world);

        await using (var db = database.CreateContext())
        {
            var skipped = await PushTesting.Processor(db, fcm).SkipPendingAsync("push is disabled: Fcm:ServiceAccountJson is not set", CancellationToken.None);
            Assert.Equal(2, skipped);
        }

        Assert.Empty(fcm.Requests);
        Assert.All(await Outbox(), o =>
        {
            Assert.Equal(PushOutboxStatus.Skipped, o.Status);
            Assert.Equal("push is disabled: Fcm:ServiceAccountJson is not set", o.LastError);
        });
    }

    [Fact]
    public async Task Without_credentials_the_worker_warns_once_at_startup_and_skips_queued_pushes_instead_of_sending()
    {
        var world = await SeedWorld();
        await Followed(world);
        var disabled = FcmConnection.Disabled("Fcm:ServiceAccountJson is not set");
        var services = new ServiceCollection();
        services.AddDbContext<ApplicationDbContext>(o => o.UseSqlServer(database.ConnectionString));
        services.AddSingleton<IPushService>(new FcmPushService(new HttpClient(fcm) { BaseAddress = FcmPushService.BaseAddress }, disabled));
        services.AddScoped<PushTargetResolver>();
        services.AddScoped<PushOutboxProcessor>();
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
        services.AddOptions<PushDeliveryOptions>();
        await using var provider = services.BuildServiceProvider();
        var logger = new ListLogger<PushNotificationWorker>();
        var worker = new PushNotificationWorker(provider.GetRequiredService<IServiceScopeFactory>(), disabled, new PushOutboxSignal(),
            Microsoft.Extensions.Options.Options.Create(new PushDeliveryOptions()), TimeProvider.System, logger);

        await worker.StartAsync(CancellationToken.None);
        // Up to 60 s: the loop ends as soon as the row is skipped, so this only matters when the machine is busy
        // (it failed once at 10 s while a web build ran alongside the full suite).
        for (var waited = 0; waited < 600 && (await Outbox()).Any(o => o.Status == PushOutboxStatus.Pending); waited++)
        {
            await Task.Delay(100);
        }
        await worker.StopAsync(CancellationToken.None);

        var row = Assert.Single(await Outbox());
        Assert.Equal(PushOutboxStatus.Skipped, row.Status);
        Assert.Equal("push is disabled: Fcm:ServiceAccountJson is not set", row.LastError);
        Assert.Empty(fcm.Requests);
        Assert.Single(logger.Warnings, w => w.StartsWith("Push notifications are disabled: Fcm:ServiceAccountJson is not set"));
        Assert.DoesNotContain(logger.Entries, e => e.Level >= LogLevel.Error);
    }

    [Fact]
    public async Task Finished_pushes_are_deleted_after_the_retention_period_and_waiting_ones_never()
    {
        var world = await SeedWorld();
        await Followed(world);
        await using var db = database.CreateContext();
        var notification = await db.Notifications.SingleAsync(n => n.UserId == world.Recipient.Id);
        var now = DateTime.UtcNow;
        PushOutboxMessage Row(PushOutboxStatus status, double ageDays)
        {
            var row = PushOutboxMessage.For(notification, world.Devices[0].Id);
            row.Status = status;
            row.CreatedAt = now.AddDays(-ageDays);
            return row;
        }
        var oldSent = Row(PushOutboxStatus.Sent, 4);
        var oldFailed = Row(PushOutboxStatus.Failed, 4);
        var oldSkipped = Row(PushOutboxStatus.Skipped, 4);
        var recentSent = Row(PushOutboxStatus.Sent, 1);
        var oldPending = Row(PushOutboxStatus.Pending, 4);
        db.PushOutbox.AddRange(oldSent, oldFailed, oldSkipped, recentSent, oldPending);
        await db.SaveChangesAsync();

        var deleted = await PushTesting.Processor(db, fcm, new PushDeliveryOptions { Retention = TimeSpan.FromDays(3) })
            .DeleteFinishedAsync(CancellationToken.None);

        Assert.Equal(3, deleted);
        var left = (await Outbox()).Select(o => o.Id).ToList();
        Assert.Contains(recentSent.Id, left);
        Assert.Contains(oldPending.Id, left);
        Assert.DoesNotContain(oldSent.Id, left);
    }

    [Fact]
    public async Task Deleting_a_notification_drops_its_waiting_pushes()
    {
        var world = await SeedWorld();
        await Followed(world);
        await using (var db = database.CreateContext())
        {
            var notification = await db.Notifications.SingleAsync(n => n.UserId == world.Recipient.Id);
            Assert.True(await new NotificationsRepository(db).DeleteNotification(notification.Id));
        }

        Assert.Empty(await Outbox());
        await Drain();
        Assert.Empty(fcm.Requests);
    }

    [Fact]
    public async Task A_notification_deleted_while_its_push_is_out_does_not_fail_the_rest_of_the_batch()
    {
        // Deleting a notification deletes its pushes, also one being sent: a chapter edit that removes a paragraph
        // deletes the notifications about its comments.
        var world = await SeedWorld();
        var secondFollower = Seed.User();
        await using (var db = database.CreateContext())
        {
            db.Users.Add(secondFollower);
            await db.SaveChangesAsync();
        }
        await Followed(world);
        await Notify(s => s.SendNewFollowerNotification(world.Recipient.Id, secondFollower));
        Guid deleted;
        await using (var db = database.CreateContext())
        {
            deleted = await db.Notifications.Where(n => n.ActorId == world.Actor.Id).Select(n => n.Id).SingleAsync();
        }
        var deletions = 0;
        fcm.Respond = _ =>
        {
            if (Interlocked.Exchange(ref deletions, 1) == 0)
            {
                using var db = database.CreateContext();
                db.Notifications.Where(n => n.Id == deleted).ExecuteDelete();
            }
            return FakeFcm.Ok();
        };

        Assert.Equal(2, await ProcessOnce(clock: ClockAfterQueueing()));

        // The other push is recorded as sent, rather than claimed again (and sent twice) when its lease ends.
        Assert.Equal(2, fcm.Requests.Count);
        var row = Assert.Single(await Outbox());
        Assert.NotEqual(deleted, row.NotificationId);
        Assert.Equal(PushOutboxStatus.Sent, row.Status);
    }

    [Fact]
    public async Task The_retry_delay_doubles_per_attempt_up_to_the_cap_and_never_undercuts_retry_after()
    {
        await using var db = database.CreateContext();
        var processor = new PushOutboxProcessor(db, PushTesting.Service(fcm), new PushTargetResolver(db),
            Microsoft.Extensions.Options.Options.Create(new PushDeliveryOptions { BaseRetryDelay = TimeSpan.FromSeconds(30), MaxRetryDelay = TimeSpan.FromMinutes(30) }),
            TimeProvider.System, NullLogger<PushOutboxProcessor>.Instance);

        foreach (var (attempt, seconds) in new[] { (1, 30), (2, 60), (3, 120), (4, 240), (7, 1800), (20, 1800) })
        {
            var delay = processor.RetryDelay(attempt, retryAfter: null);
            Assert.InRange(delay.TotalSeconds, seconds, seconds * 1.2);
        }
        Assert.Equal(TimeSpan.FromHours(1), processor.RetryDelay(1, retryAfter: TimeSpan.FromHours(1)));
    }
}
