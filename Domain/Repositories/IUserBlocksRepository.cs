using Domain.Moderation;

namespace Domain.Repositories;

/// <summary>Users blocking users (<see cref="Entities.UserBlock"/>).</summary>
public interface IUserBlocksRepository
{
    /// <summary>
    /// <paramref name="blockerId"/> blocks <paramref name="blockedId"/>; blocking again changes nothing. In the same
    /// transaction, any follow between the two (either way) is removed, and so are the notifications the blocked user
    /// caused the blocker. True when the block is new.
    /// </summary>
    Task<bool> BlockAsync(string blockerId, string blockedId, CancellationToken cancellationToken = default);

    /// <summary>Removes the block if there is one; true when there was.</summary>
    Task<bool> UnblockAsync(string blockerId, string blockedId, CancellationToken cancellationToken = default);

    Task<bool> IsBlockedAsync(string blockerId, string blockedId, CancellationToken cancellationToken = default);

    /// <summary>Whether <paramref name="viewerId"/> and <paramref name="otherUserId"/> blocked each other, either way, in one query.</summary>
    Task<BlockRelation> GetRelationAsync(string viewerId, string otherUserId, CancellationToken cancellationToken = default);

    /// <summary>The users <paramref name="blockerId"/> blocked, most recent first, with their current names.</summary>
    Task<(IReadOnlyList<BlockedUser> Users, int TotalCount)> GetBlockedUsersAsync(string blockerId, int pageNumber, int pageSize, CancellationToken cancellationToken = default);
}
