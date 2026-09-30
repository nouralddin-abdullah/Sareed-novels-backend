using Domain.Exceptions;
using Domain.Repositories;

namespace Application.Posts;

/// <summary>
/// A block cuts contact through posts (#52), as through profiles and reading lists. To a member the post's author
/// blocked, the post is unavailable: GET /api/posts/{id} answers 404 <see cref="UnavailableCode"/>, not the
/// PostNotFound of a deleted post, so the app can say «غير متاح» rather than «لم يعد موجودًا», and so do the ways to
/// read its discussion (its comments, the replies under them, and a comment's context). A member who blocked the
/// author still opens it, with authorBlockedByMe, so they can unblock. The author's post list is empty whichever of the
/// two blocked the other (GetUserPostsQueryHandler).
/// </summary>
internal static class PostBlocks
{
    public const string UnavailableCode = "PostUnavailable";

    public const string UnavailableMessage = "هذا المنشور غير متاح";

    public static NotFoundException Unavailable() => new(UnavailableMessage, UnavailableCode);

    /// <summary>
    /// Throws <see cref="Unavailable"/> when <paramref name="authorId"/>, the post's author, blocked
    /// <paramref name="viewerId"/>; an anonymous viewer and the author are never refused.
    /// </summary>
    public static async Task EnsureNotBlockedByAuthorAsync(IUserBlocksRepository blocks, string authorId,
        string? viewerId, CancellationToken cancellationToken)
    {
        if (viewerId != null && viewerId != authorId
            && await blocks.IsBlockedAsync(authorId, viewerId, cancellationToken))
        {
            throw Unavailable();
        }
    }

    /// <summary>
    /// The same for the post <paramref name="postId"/>, whose author is looked up for a signed-in viewer only. A post
    /// that doesn't exist, or was deleted, is left to the caller.
    /// </summary>
    public static async Task EnsureNotBlockedByAuthorAsync(IPostsRepository posts, IUserBlocksRepository blocks,
        Guid postId, string? viewerId, CancellationToken cancellationToken)
    {
        if (viewerId != null && await posts.GetAuthorIdAsync(postId, cancellationToken) is { } authorId)
        {
            await EnsureNotBlockedByAuthorAsync(blocks, authorId, viewerId, cancellationToken);
        }
    }
}
