using System.Data;
using Domain.Entities;
using Domain.Repositories;
using Infrastructure.Persistence;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Repositories;

public class NotificationPreferencesRepository(ApplicationDbContext dbContext) : INotificationPreferencesRepository
{
    public async Task<NotificationPreferences> Get(string userId) =>
        await dbContext.NotificationPreferences.AsNoTracking().FirstOrDefaultAsync(p => p.UserId == userId)
        ?? new NotificationPreferences { UserId = userId };

    public async Task<NotificationPreferences> Update(string userId, bool? social, bool? chapters, bool? support)
    {
        // Changed in SQL rather than read-modify-write, so two requests switching different groups at once don't undo
        // each other; update-else-insert under a key-range lock so they can't both insert.
        var now = DateTime.UtcNow;
        await dbContext.Database.ExecuteSqlInterpolatedAsync($"""
            SET XACT_ABORT ON;
            BEGIN TRANSACTION;

            UPDATE NotificationPreferences WITH (UPDLOCK, SERIALIZABLE)
            SET Social = COALESCE({Bit(social)}, Social), Chapters = COALESCE({Bit(chapters)}, Chapters),
                Support = COALESCE({Bit(support)}, Support), UpdatedAt = {now}
            WHERE UserId = {userId};

            IF @@ROWCOUNT = 0
                INSERT INTO NotificationPreferences (UserId, Social, Chapters, Support, UpdatedAt)
                VALUES ({userId}, COALESCE({Bit(social)}, 1), COALESCE({Bit(chapters)}, 1), COALESCE({Bit(support)}, 1), {now});

            COMMIT TRANSACTION;
            """);

        return await Get(userId);
    }

    private static SqlParameter Bit(bool? value) => new() { SqlDbType = SqlDbType.Bit, Value = (object?)value ?? DBNull.Value };
}
