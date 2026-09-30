using Domain.Exceptions;
using Domain.Repositories;

namespace Application.Users;

/// <summary>
/// What two members who blocked each other can't do to each other, whichever of them blocked the other (#10, #52):
/// follow (FollowUserCommandHandler), comment on the other's posts or reply to the other's comments
/// (CreateCommentCommandHandler), or like the other's posts, comments and reviews. Each is refused with 403
/// <c>{ "code": "Blocked", "message" }</c> and changes nothing. Taking a like back is never refused, so a like from
/// before the block can be removed. Neither sees the other's posts, reviews or comments on their profile
/// (<see cref="EitherWayAsync"/>).
/// </summary>
internal static class Blocks
{
    public const string Code = "Blocked";

    /// <summary>A like refused, whichever of the two blocked the other.</summary>
    public const string LikeRefusedMessage = "لا يمكنك التفاعل مع هذا المستخدم.";

    /// <summary>
    /// Refuses (403 <see cref="Code"/>) when <paramref name="actorId"/> and <paramref name="otherId"/> blocked each
    /// other, with <paramref name="theyBlockedYou"/> or <paramref name="youBlockedThem"/> as the message; one query.
    /// Nothing is refused on one's own content.
    /// </summary>
    public static async Task EnsureNotBlockedAsync(IUserBlocksRepository blocks, string actorId, string otherId,
        string theyBlockedYou, string youBlockedThem, CancellationToken cancellationToken)
    {
        if (actorId == otherId)
        {
            return;
        }

        var relation = await blocks.GetRelationAsync(actorId, otherId, cancellationToken);
        if (relation.OtherBlockedViewer)
        {
            throw new ForbidException(theyBlockedYou, Code);
        }
        if (relation.ViewerBlockedOther)
        {
            throw new ForbidException(youBlockedThem, Code);
        }
    }

    /// <summary>
    /// Refuses a like of <paramref name="authorId"/>'s post, comment or review when the two blocked each other.
    /// </summary>
    public static Task EnsureCanLikeAsync(IUserBlocksRepository blocks, string likerId, string authorId,
        CancellationToken cancellationToken) =>
        EnsureNotBlockedAsync(blocks, likerId, authorId, LikeRefusedMessage, LikeRefusedMessage, cancellationToken);

    /// <summary>
    /// Whether the signed-in <paramref name="viewerId"/> and <paramref name="memberId"/> blocked each other, either
    /// way; never for an anonymous viewer (null) or the member themselves. A member's posts, reviews and comments are
    /// then an empty list for the viewer, not a refusal (GetUserPostsQueryHandler, #54). One query.
    /// </summary>
    public static async Task<bool> EitherWayAsync(IUserBlocksRepository blocks, string? viewerId, string memberId,
        CancellationToken cancellationToken) =>
        viewerId != null && viewerId != memberId
        && (await blocks.GetRelationAsync(viewerId, memberId, cancellationToken)).Either;
}
