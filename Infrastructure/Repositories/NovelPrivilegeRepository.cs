using System.Data;
using Domain.Constants;
using Domain.Entities;
using Domain.Repositories;
using Infrastructure.Persistence;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Repositories;

public class NovelPrivilegeRepository(ApplicationDbContext dbContext) : INovelPrivilegeRepository
{
    /// <summary>How long a change of a novel's early access waits for another one of the same novel.</summary>
    private static readonly TimeSpan HoldTimeout = TimeSpan.FromSeconds(20);

    public async Task<NovelPrivilege?> GetByNovelIdAsync(Guid novelId) =>
        await dbContext.NovelPrivileges
            .AsNoTracking()
            .Include(p => p.Novel)
            .FirstOrDefaultAsync(p => p.NovelId == novelId);

    public async Task HoldAsync(Guid novelId)
    {
        var result = new SqlParameter("@result", SqlDbType.Int) { Direction = ParameterDirection.Output };
        await dbContext.Database.ExecuteSqlRawAsync(
            "EXEC @result = sp_getapplock @Resource = @resource, @LockMode = 'Exclusive', @LockOwner = 'Transaction', @LockTimeout = @timeout",
            result,
            new SqlParameter("@resource", SqlDbType.NVarChar, 255) { Value = $"early-access:{novelId:N}" },
            new SqlParameter("@timeout", SqlDbType.Int) { Value = (int)HoldTimeout.TotalMilliseconds });

        // 0 or 1: granted (at once or after waiting); below 0: timed out, cancelled, or chosen as a deadlock victim.
        if (result.Value is not int status || status < 0)
        {
            throw new InvalidOperationException(
                $"Could not hold the early access of novel {novelId} for a change (sp_getapplock returned {result.Value}).");
        }
    }

    public async Task<bool> CreateAsync(NovelPrivilege privilege)
    {
        dbContext.NovelPrivileges.Add(privilege);
        try
        {
            await dbContext.SaveChangesAsync();
            return true;
        }
        catch (DbUpdateException ex) when (ex.InnerException is SqlException { Number: 2601 or 2627 })
        {
            // The unique index on NovelId: the novel has a row already.
            dbContext.Entry(privilege).State = EntityState.Detached;
            return false;
        }
    }

    public async Task<bool> TurnOnAsync(Guid novelId, decimal subscriptionCost, int? earlyAccessDays, bool subscribersOnly,
        DateTime now) =>
        await dbContext.NovelPrivileges
            .Where(p => p.NovelId == novelId && !p.IsEnabled)
            .ExecuteUpdateAsync(s => s
                .SetProperty(p => p.IsEnabled, true)
                .SetProperty(p => p.SubscriptionCost, subscriptionCost)
                .SetProperty(p => p.EarlyAccessDays, earlyAccessDays)
                .SetProperty(p => p.SubscribersOnly, subscribersOnly)
                .SetProperty(p => p.UpdatedAt, now)) > 0;

    public async Task<bool> UpdateSettingsAsync(Guid novelId, decimal subscriptionCost, int? earlyAccessDays,
        bool subscribersOnly, DateTime now) =>
        await dbContext.NovelPrivileges
            .Where(p => p.NovelId == novelId && p.IsEnabled)
            .ExecuteUpdateAsync(s => s
                .SetProperty(p => p.SubscriptionCost, subscriptionCost)
                .SetProperty(p => p.EarlyAccessDays, earlyAccessDays)
                .SetProperty(p => p.SubscribersOnly, subscribersOnly)
                .SetProperty(p => p.UpdatedAt, now)) > 0;

    public async Task<bool> TurnOffAsync(Guid novelId, DateTime now) =>
        await dbContext.NovelPrivileges
            .Where(p => p.NovelId == novelId && p.IsEnabled)
            .ExecuteUpdateAsync(s => s
                .SetProperty(p => p.IsEnabled, false)
                .SetProperty(p => p.UpdatedAt, now)) > 0;

    public async Task<List<PublishedChapterLock>> GetPublishedLocksAsync(Guid novelId) =>
        await dbContext.Chapters
            .AsNoTracking()
            .Where(c => c.NovelId == novelId && c.Status == ChapterStatuses.Published && c.PublishedChapterSequence != null)
            .OrderBy(c => c.PublishedChapterSequence)
            .Select(c => new PublishedChapterLock(c.Id, c.PublishedChapterSequence!.Value, c.EarlyAccessFrom, c.EarlyAccessFreedAt))
            .ToListAsync();

    public async Task<StoredChapterLock?> GetChapterLockAsync(Guid chapterId) =>
        await dbContext.Chapters
            .AsNoTracking()
            .Where(c => c.Id == chapterId)
            .Select(c => new StoredChapterLock(c.NovelId, c.Status == ChapterStatuses.Published, c.EarlyAccessFrom, c.EarlyAccessFreedAt))
            .FirstOrDefaultAsync();

    public async Task<int> LockFromPositionAsync(Guid novelId, int fromSequence, DateTime now)
    {
        await dbContext.Chapters
            .Where(c => c.NovelId == novelId && (c.EarlyAccessFrom != null || c.EarlyAccessFreedAt != null))
            .ExecuteUpdateAsync(s => s
                .SetProperty(c => c.EarlyAccessFrom, (DateTime?)null)
                .SetProperty(c => c.EarlyAccessFreedAt, (DateTime?)null));

        return await dbContext.Chapters
            .Where(c => c.NovelId == novelId && c.Status == ChapterStatuses.Published && c.PublishedChapterSequence >= fromSequence)
            .ExecuteUpdateAsync(s => s.SetProperty(c => c.EarlyAccessFrom, now));
    }

    public async Task<bool> LockCameOutAsync(Guid chapterId, int freeChapters) =>
        await dbContext.Chapters
            .Where(c => c.Id == chapterId
                        && c.Status == ChapterStatuses.Published
                        && c.PublishedAt != null
                        && c.EarlyAccessFrom == null
                        && c.EarlyAccessFreedAt == null
                        && c.PublishedChapterSequence > freeChapters
                        && dbContext.NovelPrivileges.Any(p => p.NovelId == c.NovelId && p.IsEnabled))
            .ExecuteUpdateAsync(s => s.SetProperty(c => c.EarlyAccessFrom, c => c.PublishedAt)) > 0;

    public async Task<bool> FreeAsync(Guid chapterId, DateTime now) =>
        await dbContext.Chapters
            .Where(c => c.Id == chapterId && c.EarlyAccessFrom != null && c.EarlyAccessFreedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(c => c.EarlyAccessFreedAt, now)) > 0;

    public async Task<int> FreeBeforeAsync(Guid novelId, int beforeSequence, DateTime now) =>
        await dbContext.Chapters
            .Where(c => c.NovelId == novelId
                        && c.Status == ChapterStatuses.Published
                        && c.PublishedChapterSequence < beforeSequence
                        && c.EarlyAccessFrom != null
                        && c.EarlyAccessFreedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(c => c.EarlyAccessFreedAt, now));

    public async Task<int> FreezeEndedAsync(Guid novelId, DateTime startedAtOrBefore, DateTime now) =>
        await dbContext.Chapters
            .Where(c => c.NovelId == novelId
                        && c.EarlyAccessFrom != null
                        && c.EarlyAccessFrom <= startedAtOrBefore
                        && c.EarlyAccessFreedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(c => c.EarlyAccessFreedAt, now));
}
