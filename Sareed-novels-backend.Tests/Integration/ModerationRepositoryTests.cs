using Domain.Constants;
using Domain.Entities;
using Domain.Moderation;
using Infrastructure.Push;
using Infrastructure.Repositories;
using Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace Sareed_novels_backend.Tests.Integration;

/// <summary>
/// The moderation pieces below the API, on a real SQL Server: the one place notifications check blocks (pushes
/// included), and suspensions against the token check.
/// </summary>
public class ModerationRepositoryTests(SqlServerDatabase database) : IClassFixture<SqlServerDatabase>
{
    private static readonly DateTime Noon = new(2026, 9, 27, 12, 0, 0, DateTimeKind.Utc);

    private sealed class Clock(DateTime utcNow) : TimeProvider
    {
        public DateTime UtcNow { get; set; } = utcNow;
        public override DateTimeOffset GetUtcNow() => new(DateTime.SpecifyKind(UtcNow, DateTimeKind.Utc));
    }

    private async Task<User[]> SeedUsers(int count)
    {
        var users = Enumerable.Range(0, count).Select(_ => Seed.User()).ToArray();
        await using var db = database.CreateContext();
        db.Users.AddRange(users);
        await db.SaveChangesAsync();
        return users;
    }

    private async Task AddDevice(User user)
    {
        await using var db = database.CreateContext();
        db.UserDevices.Add(new UserDevice
        {
            Id = Guid.NewGuid(), UserId = user.Id, Token = "token-" + Guid.NewGuid().ToString("N"), Platform = DevicePlatforms.Android,
            CreatedAt = DateTime.UtcNow, LastSeenAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();
    }

    private static Notification Like(User recipient, User actor) => new()
    {
        Id = Guid.NewGuid(),
        UserId = recipient.Id,
        Type = NotificationType.LikeOnPost,
        ActorId = actor.Id,
        ActorDisplayName = actor.DisplayName,
        Message = $"{actor.DisplayName} أعجب بمنشورك",
        ActionUrl = "/profile/someone",
        RelatedEntityId = Guid.NewGuid(),
        RelatedEntityType = "Post",
        CreatedAt = DateTime.UtcNow
    };

    [Fact]
    public async Task No_notification_or_push_is_created_for_a_recipient_who_blocked_its_actor()
    {
        var users = await SeedUsers(4);
        var (me, blocked, other, third) = (users[0], users[1], users[2], users[3]);
        await AddDevice(me);
        await using (var db = database.CreateContext())
        {
            Assert.True(await new UserBlocksRepository(db, TimeProvider.System).BlockAsync(me.Id, blocked.Id));
        }

        var skipped = new[] { Like(me, blocked), Like(me, blocked), Like(me, blocked) };
        var created = new[] { Like(me, other), Like(third, blocked), Like(me, other) };
        await using (var db = database.CreateContext())
        {
            var notifications = new NotificationsRepository(db, new PushOutboxSignal());
            Assert.False(await notifications.CreateNotification(skipped[0]));
            Assert.False(await notifications.CreateUnlessUnreadExists(skipped[1]));
            Assert.True(await notifications.CreateNotification(created[0]));
            // A fan-out leaves out only the recipients who blocked the actor.
            await notifications.CreateNotifications([skipped[2], created[1], created[2]]);
        }

        await using var check = database.CreateContext();
        var saved = await check.Notifications.AsNoTracking().Select(n => n.Id).ToListAsync();
        Assert.All(skipped, n => Assert.DoesNotContain(n.Id, saved));
        Assert.All(created, n => Assert.Contains(n.Id, saved));
        var skippedIds = skipped.Select(n => n.Id).ToList();
        Assert.False(await check.PushOutbox.AnyAsync(o => skippedIds.Contains(o.NotificationId)));
        Assert.Equal(2, await check.PushOutbox.CountAsync(o => o.NotificationId == created[0].Id || o.NotificationId == created[2].Id));
    }

    [Fact]
    public async Task Blocking_again_changes_nothing_and_the_relation_is_read_both_ways()
    {
        var users = await SeedUsers(2);
        var (me, them) = (users[0], users[1]);
        await using var db = database.CreateContext();
        var blocks = new UserBlocksRepository(db, TimeProvider.System);

        Assert.True(await blocks.BlockAsync(me.Id, them.Id));
        Assert.False(await blocks.BlockAsync(me.Id, them.Id));

        Assert.Equal(new BlockRelation(ViewerBlockedOther: true, OtherBlockedViewer: false), await blocks.GetRelationAsync(me.Id, them.Id));
        Assert.Equal(new BlockRelation(ViewerBlockedOther: false, OtherBlockedViewer: true), await blocks.GetRelationAsync(them.Id, me.Id));
        Assert.True(await blocks.UnblockAsync(me.Id, them.Id));
        Assert.False(await blocks.UnblockAsync(me.Id, them.Id));
        Assert.False((await blocks.GetRelationAsync(me.Id, them.Id)).Either);
    }

    private static TokenCutoffCache NewCache() => new(new MemoryCache(new MemoryCacheOptions { SizeLimit = 100 }));

    private (AccountSuspensionService Suspensions, TokenRevocationService Tokens) Services(TokenCutoffCache cache, TimeProvider clock)
    {
        var db = database.CreateContext();
        var tokens = new TokenRevocationService(db, cache, clock);
        return (new AccountSuspensionService(db, tokens, cache), tokens);
    }

    [Fact]
    public async Task While_suspended_every_token_is_refused_even_one_issued_after_the_cut_off()
    {
        var user = (await SeedUsers(1))[0];
        var clock = new Clock(Noon);
        var (suspensions, tokens) = Services(NewCache(), clock);

        var until = await suspensions.SuspendAsync(user.Id, Noon.AddDays(3));

        Assert.Equal(Noon.AddDays(3), until);
        Assert.False(await tokens.IsTokenActiveAsync(user.Id, Noon.AddMinutes(-5)));
        // Issued after the suspension began (the revocation alone would let this one through).
        Assert.False(await tokens.IsTokenActiveAsync(user.Id, Noon.AddSeconds(10)));

        // It ends by itself: tokens issued since then work (and older ones stay revoked).
        clock.UtcNow = Noon.AddDays(3).AddMinutes(1);
        var (_, later) = Services(NewCache(), clock);
        Assert.True(await later.IsTokenActiveAsync(user.Id, Noon.AddDays(3).AddSeconds(30)));
        Assert.False(await later.IsTokenActiveAsync(user.Id, Noon.AddMinutes(-5)));
    }

    [Fact]
    public async Task Lifting_applies_at_once_even_while_the_suspension_is_cached()
    {
        var user = (await SeedUsers(1))[0];
        var cache = NewCache();
        var clock = new Clock(Noon);
        var (suspensions, tokens) = Services(cache, clock);
        await suspensions.SuspendAsync(user.Id, Suspension.Permanent);
        Assert.False(await tokens.IsTokenActiveAsync(user.Id, Noon.AddMinutes(1))); // now cached

        Assert.True(await suspensions.LiftAsync(user.Id));

        Assert.True(await tokens.IsTokenActiveAsync(user.Id, Noon.AddMinutes(1)));
        Assert.False(await suspensions.LiftAsync(Guid.NewGuid().ToString()));
    }

    [Fact]
    public async Task A_suspension_is_only_ever_made_longer_and_unknown_users_are_not_suspended()
    {
        var user = (await SeedUsers(1))[0];
        var (suspensions, _) = Services(NewCache(), new Clock(Noon));

        Assert.Equal(Noon.AddDays(10), await suspensions.SuspendAsync(user.Id, Noon.AddDays(10)));
        Assert.Equal(Noon.AddDays(10), await suspensions.SuspendAsync(user.Id, Noon.AddDays(1)));
        Assert.Equal(Suspension.Permanent, await suspensions.SuspendAsync(user.Id, Suspension.Permanent));
        Assert.Equal(Suspension.Permanent, await suspensions.SuspendAsync(user.Id, Noon.AddDays(30)));

        await suspensions.LiftAsync(user.Id);
        Assert.Equal(Noon.AddDays(1), await suspensions.SuspendAsync(user.Id, Noon.AddDays(1)));
        Assert.Null(await suspensions.SuspendAsync(Guid.NewGuid().ToString(), Noon.AddDays(1)));
    }
}
