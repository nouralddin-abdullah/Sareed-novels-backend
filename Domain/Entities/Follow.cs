namespace Domain.Entities;

public class Follow
{
    public string FollowerId { get; set; } = default!;
    public User Follower { get; set; } = default!;
    public string FollowedId { get; set; } = default!;
    public User Followed { get; set; } = default!;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
