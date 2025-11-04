namespace Domain.Entities;

public class ReadingListFollower
{
    public Guid ReadingListId { get; set; }
    public ReadingList ReadingList { get; set; } = default!;

    public string UserId { get; set; } = default!;
    public User User { get; set; } = default!;

    public DateTime FollowedAt { get; set; } = DateTime.UtcNow;
}