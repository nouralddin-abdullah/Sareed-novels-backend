namespace Domain.Entities;

/// <summary>
/// <see cref="BlockerId"/> blocked <see cref="BlockedId"/>: the blocked user's comments, replies, reviews and posts are
/// left out of the blocker's lists, and they can't follow the blocker, answer or comment on the blocker's comments and
/// posts, notify the blocker or open the blocker's profile.
/// </summary>
public class UserBlock
{
    public string BlockerId { get; set; } = default!;
    public string BlockedId { get; set; } = default!;
    public DateTime CreatedAt { get; set; }
}
