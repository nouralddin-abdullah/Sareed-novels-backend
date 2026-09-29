using Domain.Entities;
using Domain.Moderation;
using Domain.Repositories;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Repositories;

public class UserBlocksRepository(ApplicationDbContext dbContext, TimeProvider time) : IUserBlocksRepository
{
    public async Task<bool> BlockAsync(string blockerId, string blockedId, CancellationToken cancellationToken = default)
    {
        int added;
        await using (var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken))
        {
            // Idempotent under concurrent requests: the key lock makes a second insert of the same pair a no-op.
            added = await dbContext.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO UserBlocks (BlockerId, BlockedId, CreatedAt)
                SELECT {blockerId}, {blockedId}, {time.GetUtcNow().UtcDateTime}
                WHERE NOT EXISTS (SELECT 1 FROM UserBlocks WITH (UPDLOCK, HOLDLOCK)
                                  WHERE BlockerId = {blockerId} AND BlockedId = {blockedId})
                """, cancellationToken);

            // Follower and following totals are counted from Follows, so removing the rows is the whole change.
            await dbContext.Follows
                .Where(f => (f.FollowerId == blockerId && f.FollowedId == blockedId)
                            || (f.FollowerId == blockedId && f.FollowedId == blockerId))
                .ExecuteDeleteAsync(cancellationToken);

            await transaction.CommitAsync(cancellationToken);
        }

        // What the blocked user caused the blocker so far (replies, likes, follows...) goes too, their pushes with
        // them (cascade). After the commit, not in the transaction: from then on no new one can be created (the
        // notification inserts check for the block while holding their own locks, which would deadlock with this).
        await dbContext.Notifications
            .Where(n => n.UserId == blockerId && n.ActorId == blockedId)
            .ExecuteDeleteAsync(cancellationToken);

        return added == 1;
    }

    public async Task<bool> UnblockAsync(string blockerId, string blockedId, CancellationToken cancellationToken = default) =>
        await dbContext.UserBlocks
            .Where(b => b.BlockerId == blockerId && b.BlockedId == blockedId)
            .ExecuteDeleteAsync(cancellationToken) > 0;

    public Task<bool> IsBlockedAsync(string blockerId, string blockedId, CancellationToken cancellationToken = default) =>
        dbContext.UserBlocks.AnyAsync(b => b.BlockerId == blockerId && b.BlockedId == blockedId, cancellationToken);

    public async Task<BlockRelation> GetRelationAsync(string viewerId, string otherUserId, CancellationToken cancellationToken = default)
    {
        var blockers = await dbContext.UserBlocks
            .AsNoTracking()
            .Where(b => (b.BlockerId == viewerId && b.BlockedId == otherUserId)
                        || (b.BlockerId == otherUserId && b.BlockedId == viewerId))
            .Select(b => b.BlockerId)
            .ToListAsync(cancellationToken);

        return new BlockRelation(
            ViewerBlockedOther: blockers.Contains(viewerId),
            OtherBlockedViewer: blockers.Contains(otherUserId));
    }

    public async Task<IReadOnlySet<string>> GetBlockedEitherWayAsync(string viewerId, IReadOnlyCollection<string> otherUserIds,
        CancellationToken cancellationToken = default)
    {
        var others = otherUserIds.Where(id => id != viewerId).Distinct().ToList();
        if (others.Count == 0)
        {
            return new HashSet<string>();
        }

        var blocked = await dbContext.UserBlocks
            .AsNoTracking()
            .Where(b => (b.BlockerId == viewerId && others.Contains(b.BlockedId))
                        || (b.BlockedId == viewerId && others.Contains(b.BlockerId)))
            .Select(b => b.BlockerId == viewerId ? b.BlockedId : b.BlockerId)
            .ToListAsync(cancellationToken);
        return blocked.ToHashSet();
    }

    public async Task<(IReadOnlyList<BlockedUser> Users, int TotalCount)> GetBlockedUsersAsync(
        string blockerId, int pageNumber, int pageSize, CancellationToken cancellationToken = default)
    {
        var blocks = dbContext.UserBlocks.AsNoTracking().Where(b => b.BlockerId == blockerId);
        var totalCount = await blocks.CountAsync(cancellationToken);

        var users = await blocks
            .Join(dbContext.Users, b => b.BlockedId, u => u.Id,
                (b, u) => new { b.CreatedAt, u.Id, u.UserName, u.DisplayName, u.ProfilePhoto })
            .OrderByDescending(x => x.CreatedAt)
            .ThenBy(x => x.Id)
            .Skip((pageNumber - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        return (users.Select(u => new BlockedUser(u.Id, u.UserName!, u.DisplayName, u.ProfilePhoto, u.CreatedAt)).ToList(), totalCount);
    }
}
