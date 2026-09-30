namespace Domain.Entities;

/// <summary>
/// <see cref="BlockerId"/> blocked <see cref="BlockedId"/>: the blocked user's comments, replies, reviews and posts are
/// left out of the blocker's lists, and the blocker's profile, reading lists and posts don't open for them. Whichever
/// of the two blocked the other, neither can follow the other, like the other's posts, comments or reviews, comment on
/// the other's posts or reply to the other's comments, and no notification passes between them, except that a gift or
/// a subscription from the blocker still reaches the blocked user (<see cref="Constants.NotificationBlocking"/>).
/// </summary>
public class UserBlock
{
    public string BlockerId { get; set; } = default!;
    public string BlockedId { get; set; } = default!;
    public DateTime CreatedAt { get; set; }
}
