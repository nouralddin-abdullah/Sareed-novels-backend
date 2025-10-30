namespace Domain.Entities;

public class CommentLikes
{
    public int Id { get; set; }
    public string UserId { get; set; } = default!;
    public User User { get; set; } = default!;
    public Guid CommentId { get; set; }
    public Comments Comment { get; set; } = default!;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
