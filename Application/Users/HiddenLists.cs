using Domain.Entities;
using Domain.Exceptions;
using Domain.Profiles;

namespace Application.Users;

/// <summary>
/// A member's review or comment list hidden from others (#61): with <see cref="ListVisibility.OnlyMe"/> the list is
/// theirs alone. Anyone else, signed in or not, admins included, is refused it (403 <see cref="Code"/>), after the
/// list's 404 UserNotFound and before its block rule (an empty page); the profile says so (reviewsHidden,
/// commentsHidden). The counts on the profile stay as they are (the owner's decision), and each review and comment stays
/// where it was written. Moderation and admin tools don't go through here.
/// </summary>
internal static class HiddenLists
{
    public const string Code = "ListHidden";
    public const string ReviewsMessage = "اختار صاحب الحساب إخفاء مراجعاته";
    public const string CommentsMessage = "اختار صاحب الحساب إخفاء تعليقاته";

    /// <summary>
    /// Whether a list of <paramref name="member"/>'s set to <paramref name="visibility"/> is hidden from
    /// <paramref name="viewerId"/> (null when signed out): from everyone but the member, when it is theirs alone.
    /// </summary>
    public static bool IsHiddenFrom(ListVisibility visibility, User member, string? viewerId) =>
        visibility == ListVisibility.OnlyMe && viewerId != member.Id;

    /// <summary>Refuses the member's review list (403 ListHidden) to a viewer it is hidden from.</summary>
    public static void EnsureReviewsShownTo(User member, string? viewerId)
    {
        if (IsHiddenFrom(member.ReviewsVisibility, member, viewerId))
        {
            throw new ForbidException(ReviewsMessage, Code);
        }
    }

    /// <summary>Refuses the member's comment list (403 ListHidden) to a viewer it is hidden from.</summary>
    public static void EnsureCommentsShownTo(User member, string? viewerId)
    {
        if (IsHiddenFrom(member.CommentsVisibility, member, viewerId))
        {
            throw new ForbidException(CommentsMessage, Code);
        }
    }
}
