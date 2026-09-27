using Domain.Entities;
using Infrastructure.Repositories;
using Microsoft.EntityFrameworkCore;

namespace Sareed_novels_backend.Tests.Integration;

public class PushDeviceAndPreferenceTests(SqlServerDatabase database) : IClassFixture<SqlServerDatabase>
{
    private async Task<User[]> SeedUsers(int count)
    {
        await using var db = database.CreateContext();
        var users = Enumerable.Range(0, count).Select(_ => Seed.User()).ToArray();
        db.Users.AddRange(users);
        await db.SaveChangesAsync();
        return users;
    }

    private static string NewToken() => $"{Guid.NewGuid():N}:APA91b{Guid.NewGuid():N}{Guid.NewGuid():N}";

    private static UserDevice Device(User user, string token, DateTime seenAt, string? appVersion = "1.0.0") => new()
    {
        UserId = user.Id,
        Token = token,
        Platform = "android",
        AppVersion = appVersion,
        Locale = "ar",
        CreatedAt = seenAt,
        LastSeenAt = seenAt
    };

    private async Task Register(UserDevice device)
    {
        await using var db = database.CreateContext();
        await new UserDevicesRepository(db).Upsert(device);
    }

    private async Task<List<UserDevice>> DevicesWith(string token)
    {
        await using var db = database.CreateContext();
        return await db.UserDevices.AsNoTracking().Where(d => d.Token == token).ToListAsync();
    }

    [Fact]
    public async Task Registering_a_token_again_updates_its_row_instead_of_adding_one()
    {
        var (user, token) = ((await SeedUsers(1))[0], NewToken());
        var first = DateTime.UtcNow.AddDays(-3);
        await Register(Device(user, token, first, "1.0.0"));
        await Register(Device(user, token, first.AddDays(2), "1.1.0"));

        var device = Assert.Single(await DevicesWith(token));
        Assert.Equal(user.Id, device.UserId);
        Assert.Equal("1.1.0", device.AppVersion);
        Assert.Equal(first, device.CreatedAt, TimeSpan.FromMilliseconds(1));
        Assert.Equal(first.AddDays(2), device.LastSeenAt, TimeSpan.FromMilliseconds(1));
    }

    [Fact]
    public async Task A_token_signed_into_by_another_user_moves_to_that_user()
    {
        var users = await SeedUsers(2);
        var token = NewToken();
        await Register(Device(users[0], token, DateTime.UtcNow.AddHours(-1)));

        await Register(Device(users[1], token, DateTime.UtcNow));

        Assert.Equal(users[1].Id, Assert.Single(await DevicesWith(token)).UserId);
    }

    [Fact]
    public async Task Concurrent_registrations_of_one_token_leave_one_row()
    {
        var users = await SeedUsers(3);
        var token = NewToken();

        await Task.WhenAll(Enumerable.Range(0, 12).Select(i => Register(Device(users[i % 3], token, DateTime.UtcNow))));

        Assert.Single(await DevicesWith(token));
    }

    [Fact]
    public async Task A_user_keeps_at_most_ten_devices_dropping_the_least_recently_seen()
    {
        var user = (await SeedUsers(1))[0];
        var start = DateTime.UtcNow.AddDays(-30);
        var tokens = Enumerable.Range(0, UserDevicesRepository.MaxDevicesPerUser + 1).Select(_ => NewToken()).ToList();
        for (var i = 0; i < tokens.Count; i++)
        {
            await Register(Device(user, tokens[i], start.AddDays(i)));
        }

        await using var db = database.CreateContext();
        var kept = await db.UserDevices.Where(d => d.UserId == user.Id).Select(d => d.Token).ToListAsync();
        Assert.Equal(UserDevicesRepository.MaxDevicesPerUser, kept.Count);
        Assert.DoesNotContain(tokens[0], kept);
    }

    [Fact]
    public async Task Unregistering_removes_only_the_callers_own_token_and_is_idempotent()
    {
        var users = await SeedUsers(2);
        var (mine, theirs) = (NewToken(), NewToken());
        await Register(Device(users[0], mine, DateTime.UtcNow));
        await Register(Device(users[1], theirs, DateTime.UtcNow));

        await using (var db = database.CreateContext())
        {
            var repository = new UserDevicesRepository(db);
            await repository.Remove(users[0].Id, mine);
            await repository.Remove(users[0].Id, mine);
            await repository.Remove(users[0].Id, theirs);
        }

        Assert.Empty(await DevicesWith(mine));
        Assert.Single(await DevicesWith(theirs));
    }

    [Fact]
    public async Task Preferences_start_all_on_and_an_update_changes_only_the_groups_it_names()
    {
        var user = (await SeedUsers(1))[0];
        await using var db = database.CreateContext();
        var repository = new NotificationPreferencesRepository(db);

        var defaults = await repository.Get(user.Id);
        Assert.True(defaults.Social && defaults.Chapters && defaults.Support);

        var afterFirst = await repository.Update(user.Id, social: null, chapters: false, support: null);
        Assert.Equal((true, false, true), (afterFirst.Social, afterFirst.Chapters, afterFirst.Support));

        var afterSecond = await repository.Update(user.Id, social: false, chapters: null, support: null);
        Assert.Equal((false, false, true), (afterSecond.Social, afterSecond.Chapters, afterSecond.Support));

        var stored = await repository.Get(user.Id);
        Assert.Equal((false, false, true), (stored.Social, stored.Chapters, stored.Support));
    }

    [Fact]
    public async Task Concurrent_updates_of_different_groups_both_stick()
    {
        var user = (await SeedUsers(1))[0];

        async Task Update(bool? social, bool? chapters, bool? support)
        {
            await using var db = database.CreateContext();
            await new NotificationPreferencesRepository(db).Update(user.Id, social, chapters, support);
        }

        await Task.WhenAll(Update(false, null, null), Update(null, false, null), Update(null, null, false));

        await using var check = database.CreateContext();
        var stored = await new NotificationPreferencesRepository(check).Get(user.Id);
        Assert.Equal((false, false, false), (stored.Social, stored.Chapters, stored.Support));
    }

    [Fact]
    public async Task Deleting_a_user_deletes_their_devices_and_preferences()
    {
        var user = (await SeedUsers(1))[0];
        var token = NewToken();
        await Register(Device(user, token, DateTime.UtcNow));
        await using (var db = database.CreateContext())
        {
            await new NotificationPreferencesRepository(db).Update(user.Id, false, null, null);
        }

        await using (var db = database.CreateContext())
        {
            db.Users.Remove(await db.Users.SingleAsync(u => u.Id == user.Id));
            await db.SaveChangesAsync();
        }

        await using var check = database.CreateContext();
        Assert.Empty(await DevicesWith(token));
        Assert.False(await check.NotificationPreferences.AnyAsync(p => p.UserId == user.Id));
    }
}
