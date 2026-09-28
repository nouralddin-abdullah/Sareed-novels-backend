using Domain.Entities;
using Domain.Repositories;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Repositories;

public class UserDevicesRepository(ApplicationDbContext dbContext) : IUserDevicesRepository
{
    /// <summary>
    /// Devices kept per user; registering one more drops the least recently seen. Real users have a phone or two,
    /// and tokens of uninstalled apps are removed when FCM reports them; this only bounds a misbehaving client.
    /// </summary>
    internal const int MaxDevicesPerUser = 10;

    public async Task Upsert(UserDevice device)
    {
        var d = device;
        var id = d.Id == Guid.Empty ? Guid.NewGuid() : d.Id;
        // Update-else-insert under a key-range lock on the token, so two registrations of one token can't both insert.
        await dbContext.Database.ExecuteSqlInterpolatedAsync($"""
            SET XACT_ABORT ON;
            BEGIN TRANSACTION;

            UPDATE UserDevices WITH (UPDLOCK, SERIALIZABLE)
            SET UserId = {d.UserId}, Platform = {d.Platform}, AppVersion = {d.AppVersion}, Locale = {d.Locale},
                LastSeenAt = {d.LastSeenAt}
            WHERE Token = {d.Token};

            IF @@ROWCOUNT = 0
                INSERT INTO UserDevices (Id, UserId, Token, Platform, AppVersion, Locale, CreatedAt, LastSeenAt)
                VALUES ({id}, {d.UserId}, {d.Token}, {d.Platform}, {d.AppVersion}, {d.Locale}, {d.CreatedAt}, {d.LastSeenAt});

            DELETE FROM UserDevices
            WHERE UserId = {d.UserId} AND Id NOT IN (
                SELECT TOP ({MaxDevicesPerUser}) Id FROM UserDevices WHERE UserId = {d.UserId}
                ORDER BY LastSeenAt DESC, CreatedAt DESC);

            COMMIT TRANSACTION;
            """);
    }

    public Task Remove(string userId, string token) =>
        dbContext.UserDevices
            .Where(d => d.Token == token && d.UserId == userId)
            .ExecuteDeleteAsync();

    public Task RemoveToken(string token) =>
        dbContext.UserDevices
            .Where(d => d.Token == token)
            .ExecuteDeleteAsync();
}
