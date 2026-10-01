using Domain.Profiles;

namespace Domain.Repositories;

/// <summary>
/// The member's reviews and comments that their profile lists (#54), and how many there are: the profile's
/// reviewsCount and commentsCount are these totals, counted by the same queries as the lists. Everything here is what
/// anyone may read, signed in or not; blocks between a viewer and the member are for the caller to apply.
/// <list type="bullet">
/// <item>Reviews: those on a novel readers can open (not deleted, not a draft, with a published chapter).</item>
/// <item>
/// Comments: those on a chapter or a paragraph, top-level and replies, that aren't deleted, where readers can open
/// the chapter (published, in a novel readers can open), and, for a reply, whose parent isn't deleted. Comments on
/// posts, and replies under them, are not listed.
/// </item>
/// </list>
/// Pages go newest first; comments or reviews written at the same moment go by id, so pages never repeat or skip one.
/// </summary>
public interface IProfileListsRepository
{
    Task<(IReadOnlyList<ProfileReview> Items, int TotalCount)> GetReviewsAsync(string userId, int pageNumber, int pageSize,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// A page of the member's comments, read for <paramref name="viewerId"/> (null when signed out): a reply comes with
    /// the comment it answers (#60), unless the viewer blocked that comment's author, as the comment lists leave such
    /// comments out for them; the reply itself is listed either way, so the total is the same for everyone.
    /// </summary>
    Task<(IReadOnlyList<ProfileComment> Items, int TotalCount)> GetCommentsAsync(string userId, string? viewerId,
        int pageNumber, int pageSize, CancellationToken cancellationToken = default);

    /// <summary>The totals of <see cref="GetReviewsAsync"/> and <see cref="GetCommentsAsync"/>.</summary>
    Task<ProfileCounts> CountAsync(string userId, CancellationToken cancellationToken = default);
}
