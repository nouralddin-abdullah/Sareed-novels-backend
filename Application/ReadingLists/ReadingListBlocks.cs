using Domain.Exceptions;
using Domain.Repositories;

namespace Application.ReadingLists;

/// <summary>
/// A block cuts contact through reading lists too (#25): to a member the list's owner blocked, the list doesn't exist,
/// as the owner's profile and public lists don't. Opening or following it answers what a list nobody has answers (404
/// <see cref="NotFoundCode"/>), and it drops out of their followed lists. The blocker still sees the blocked member's
/// lists when opened directly, as they still see the member's profile (flagged), so they can unblock.
/// </summary>
internal static class ReadingListBlocks
{
    public const string NotFoundCode = "ReadingListNotFound";
    public const string NotFoundMessage = "القائمة غير موجودة";

    /// <summary>Throws the missing-list 404 when <paramref name="ownerId"/> blocked <paramref name="viewerId"/>.</summary>
    public static async Task EnsureNotBlockedByOwnerAsync(IUserBlocksRepository blocks, string ownerId, string? viewerId,
        CancellationToken cancellationToken)
    {
        if (viewerId != null && viewerId != ownerId && await blocks.IsBlockedAsync(ownerId, viewerId, cancellationToken))
        {
            throw new NotFoundException(NotFoundMessage, NotFoundCode);
        }
    }
}
