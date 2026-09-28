using Application.Services;
using Domain.Constants;
using Domain.Entities;
using Infrastructure.Services;
using Infrastructure.Services.Storage;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;

namespace Sareed_novels_backend.Tests.Integration;

/// <summary>
/// AccountDeletionService on its own: what happens when storage fails after the commit, a second deletion, and the
/// notifications it renames at the edges.
/// </summary>
public class AccountDeletionServiceTests(SqlServerDatabase database) : IClassFixture<SqlServerDatabase>
{
    private const string Bucket = "https://files.test";

    private sealed class BrokenStorage : IObjectStorage
    {
        public List<string> Attempts { get; } = [];

        public Task<string> PutAsync(string key, Stream content, string contentType, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<byte[]?> GetAsync(string keyOrUrl, long maxBytes, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task DeleteAsync(string key, CancellationToken cancellationToken = default)
        {
            Attempts.Add(key);
            throw new IOException("storage is down");
        }

        public string? KeyOf(string keyOrUrl) => StorageKeys.KeyFrom(Bucket, keyOrUrl);
    }

    private static (AccountDeletionService Service, ListLogger<AccountDeletionService> Logger) Service(
        Infrastructure.Persistence.ApplicationDbContext db, IObjectStorage storage)
    {
        var cache = new TokenCutoffCache(new MemoryCache(new MemoryCacheOptions { SizeLimit = 100 }));
        var logger = new ListLogger<AccountDeletionService>();
        var service = new AccountDeletionService(db, new UpperInvariantLookupNormalizer(),
            new TokenRevocationService(db, cache, TimeProvider.System), cache, storage, TimeProvider.System, logger);
        return (service, logger);
    }

    [Fact]
    public async Task A_storage_failure_after_the_commit_is_logged_and_the_deletion_stands()
    {
        await using var db = database.CreateContext();
        var user = Seed.User();
        user.ProfilePhoto = $"{Bucket}/profile-images/{user.Id}/photo.png";
        db.Users.Add(user);
        await db.SaveChangesAsync();
        var storage = new BrokenStorage();
        var (service, logger) = Service(db, storage);

        var result = await service.DeleteAsync(user.Id);

        Assert.True(result.Deleted);
        Assert.Equal(0, result.FilesDeleted);
        Assert.Equal(1, result.FilesNotDeleted);
        Assert.Equal([$"profile-images/{user.Id}/photo.png"], storage.Attempts);
        Assert.Contains(logger.Entries, e => e.Level == LogLevel.Error && e.Message.Contains("could not be deleted from storage"));
        db.ChangeTracker.Clear();
        var row = await db.Users.AsNoTracking().SingleAsync(u => u.Id == user.Id);
        Assert.NotNull(row.DeletedAt);
        Assert.Null(row.ProfilePhoto);
    }

    [Fact]
    public async Task Deleting_again_or_an_unknown_account_does_nothing()
    {
        await using var db = database.CreateContext();
        var user = Seed.User();
        db.Users.Add(user);
        await db.SaveChangesAsync();
        var (service, _) = Service(db, new BrokenStorage());

        Assert.True((await service.DeleteAsync(user.Id)).Deleted);
        db.ChangeTracker.Clear();
        var first = await db.Users.AsNoTracking().SingleAsync(u => u.Id == user.Id);

        Assert.False((await service.DeleteAsync(user.Id)).Deleted);
        Assert.False((await service.DeleteAsync(Guid.NewGuid().ToString())).Deleted);
        db.ChangeTracker.Clear();
        var second = await db.Users.AsNoTracking().SingleAsync(u => u.Id == user.Id);
        Assert.Equal(first.DeletedAt, second.DeletedAt);
        Assert.Equal(first.UserName, second.UserName);
        Assert.Equal(first.SecurityStamp, second.SecurityStamp);
    }

    [Fact]
    public async Task Notification_messages_lose_the_name_they_start_with_and_stay_within_their_length()
    {
        await using var db = database.CreateContext();
        var (actor, recipient) = (Seed.User(displayName: "ab"), Seed.User());
        db.Users.AddRange(actor, recipient);
        await db.SaveChangesAsync();

        Notification From(string name, string message) => new()
        {
            Id = Guid.NewGuid(), UserId = recipient.Id, Type = NotificationType.NewFollower, ActorId = actor.Id,
            ActorDisplayName = name, ActorProfilePhoto = "https://files.test/photo.png", Message = message, ActionUrl = "/x",
            CreatedAt = DateTime.UtcNow
        };
        // A short old name at the start of a message that is already as long as it can be.
        var full = From("ab", "ab " + new string('ن', 497));
        // A name they had before, and a message that doesn't start with it.
        var renamed = From("اسم قديم", "اسم قديم بدأ بمتابعتك");
        var other = From("ab", "رسالة لا تبدأ بالاسم");
        db.Notifications.AddRange(full, renamed, other);
        await db.SaveChangesAsync();

        Assert.True((await Service(db, new BrokenStorage()).Service.DeleteAsync(actor.Id)).Deleted);

        db.ChangeTracker.Clear();
        var after = await db.Notifications.AsNoTracking().Where(n => n.ActorId == actor.Id).ToDictionaryAsync(n => n.Id);
        Assert.All(after.Values, n =>
        {
            Assert.Equal(DeletedAccounts.DisplayName, n.ActorDisplayName);
            Assert.Null(n.ActorProfilePhoto);
        });
        Assert.Equal(500, after[full.Id].Message.Length);
        Assert.StartsWith(DeletedAccounts.DisplayName + " ننن", after[full.Id].Message);
        Assert.Equal(DeletedAccounts.DisplayName + " بدأ بمتابعتك", after[renamed.Id].Message);
        Assert.Equal("رسالة لا تبدأ بالاسم", after[other.Id].Message);
    }
}
