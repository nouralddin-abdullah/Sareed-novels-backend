namespace Domain.Entities;

public class PostLike
{
    public int Id { get; set; }
    public string UserId { get; set; } = default!;
    public User User { get; set; } = default!;
    public Guid PostId { get; set; }
    public Post Post { get; set; } = default!;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
