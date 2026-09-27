using System.Data.Common;
using System.Diagnostics;
using Domain.Constants;
using Domain.Entities;
using Infrastructure.Persistence;
using Infrastructure.Push;
using Infrastructure.Repositories;
using Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit.Abstractions;

namespace Sareed_novels_backend.Tests.Integration;

/// <summary>Every notification queues a push to each of its recipient's devices, in the same save.</summary>
public class PushQueueingTests(SqlServerDatabase database, ITestOutputHelper output) : IClassFixture<SqlServerDatabase>
{
    private async Task<(User Recipient, User Actor, List<UserDevice> Devices)> SeedRecipient(int devices)
    {
        await using var db = database.CreateContext();
        var (recipient, actor) = (Seed.User(), Seed.User());
        var list = Enumerable.Range(0, devices).Select(_ => new UserDevice
        {
            Id = Guid.NewGuid(),
            UserId = recipient.Id,
            Token = "token-" + Guid.NewGuid().ToString("N"),
            Platform = DevicePlatforms.Android,
            CreatedAt = DateTime.UtcNow,
            LastSeenAt = DateTime.UtcNow
        }).ToList();
        db.Users.AddRange(recipient, actor);
        db.UserDevices.AddRange(list);
        await db.SaveChangesAsync();
        return (recipient, actor, list);
    }

    private static Notification Follow(User recipient, User actor) => new()
    {
        Id = Guid.NewGuid(),
        UserId = recipient.Id,
        Type = NotificationType.NewFollower,
        ActorId = actor.Id,
        ActorDisplayName = actor.DisplayName,
        Message = $"{actor.DisplayName} بدأ بمتابعتك",
        ActionUrl = $"/profile/{actor.UserName}",
        CreatedAt = DateTime.UtcNow
    };

    private async Task<List<PushOutboxMessage>> QueuedFor(Guid notificationId)
    {
        await using var db = database.CreateContext();
        return await db.PushOutbox.AsNoTracking().Where(o => o.NotificationId == notificationId).ToListAsync();
    }

    [Fact]
    public async Task A_notification_queues_one_pending_push_per_device_of_its_recipient()
    {
        var (recipient, actor, devices) = await SeedRecipient(devices: 2);
        var notification = Follow(recipient, actor);
        var signal = new PushOutboxSignal();

        await using (var db = database.CreateContext())
        {
            await new NotificationsRepository(db, signal).CreateNotification(notification);
        }

        var queued = await QueuedFor(notification.Id);
        Assert.Equal(devices.Select(d => d.Id).Order(), queued.Select(q => q.DeviceId).Order());
        Assert.All(queued, q =>
        {
            Assert.Equal(PushOutboxStatus.Pending, q.Status);
            Assert.Equal(0, q.Attempts);
            Assert.Equal(notification.CreatedAt, q.NextAttemptAt, TimeSpan.FromMilliseconds(1));
        });

        // The worker is woken right away instead of waiting for its next poll.
        var waited = Stopwatch.StartNew();
        await signal.WaitAsync(TimeSpan.FromSeconds(10), CancellationToken.None);
        Assert.True(waited.Elapsed < TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task A_recipient_without_the_app_gets_the_notification_but_no_queued_push()
    {
        var (recipient, actor, _) = await SeedRecipient(devices: 0);
        var notification = Follow(recipient, actor);

        await using (var db = database.CreateContext())
        {
            await new NotificationsRepository(db).CreateNotification(notification);
        }

        await using var check = database.CreateContext();
        Assert.True(await check.Notifications.AnyAsync(n => n.Id == notification.Id));
        Assert.Empty(await QueuedFor(notification.Id));
    }

    [Fact]
    public async Task A_deduplicated_like_or_follow_queues_a_push_only_when_it_is_inserted()
    {
        var (recipient, actor, _) = await SeedRecipient(devices: 1);
        var first = Follow(recipient, actor);
        var repeat = Follow(recipient, actor);

        await using (var db = database.CreateContext())
        {
            var repository = new NotificationsRepository(db, new PushOutboxSignal());
            Assert.True(await repository.CreateUnlessUnreadExists(first));
            Assert.False(await repository.CreateUnlessUnreadExists(repeat));
        }

        Assert.Single(await QueuedFor(first.Id));
        Assert.Empty(await QueuedFor(repeat.Id));
    }

    [Fact]
    public async Task A_new_chapter_for_1200_readers_is_inserted_in_batches_with_pushes_only_for_readers_with_the_app()
    {
        await using var seed = database.CreateContext();
        var author = Seed.User();
        var novel = Seed.Novel(author, "رواية " + Seed.Marker());
        var chapter = Seed.Chapters(novel, 1, DateTime.UtcNow)[0];
        var readers = Enumerable.Range(0, 1200).Select(_ => Seed.User()).ToList();
        seed.Users.Add(author);
        seed.Users.AddRange(readers);
        seed.Novels.Add(novel);
        seed.Chapters.Add(chapter);
        var withApp = readers.Take(3).Select(r => new UserDevice
        {
            Id = Guid.NewGuid(), UserId = r.Id, Token = "token-" + Guid.NewGuid().ToString("N"), Platform = "android",
            CreatedAt = DateTime.UtcNow, LastSeenAt = DateTime.UtcNow
        }).ToList();
        seed.UserDevices.AddRange(withApp);
        await seed.SaveChangesAsync();

        var commands = new CommandCounter();
        await using (var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
                         .UseSqlServer(database.ConnectionString).AddInterceptors(commands).Options))
        {
            var service = new NotificationService(NullLogger<NotificationService>.Instance, new NotificationsRepository(db));
            await service.SendNewChapterInLibraryNotification(readers.Select(r => r.Id).ToList(), novel, chapter);
        }

        await using var check = database.CreateContext();
        var notified = await check.Notifications.CountAsync(n => n.RelatedEntityId == chapter.Id && n.Type == NotificationType.NewChapterInLibrary);
        Assert.Equal(1200, notified);
        var queued = await check.PushOutbox.Join(check.Notifications, o => o.NotificationId, n => n.Id, (o, n) => n)
            .Where(n => n.RelatedEntityId == chapter.Id).Select(n => n.UserId).ToListAsync();
        Assert.Equal(withApp.Select(d => d.UserId).Order(), queued.Order());

        // Was a round trip per reader; now a few batched commands per 500 readers.
        output.WriteLine($"{commands.Count} database commands for 1200 readers");
        Assert.InRange(commands.Count, 1, 100);
    }

    private sealed class CommandCounter : DbCommandInterceptor
    {
        private int count;

        public int Count => count;

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData eventData,
            InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref count);
            return base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
        }

        public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(DbCommand command, CommandEventData eventData,
            InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref count);
            return base.NonQueryExecutingAsync(command, eventData, result, cancellationToken);
        }
    }
}
