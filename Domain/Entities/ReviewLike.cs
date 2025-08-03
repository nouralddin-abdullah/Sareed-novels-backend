namespace Domain.Entities;

public class ReviewLike
{
    public int Id { get; set; }
    public string UserId { get; set; } = default!;
    public User User { get; set; } = default!;
    public Guid ReviewId { get; set; } = default!;
    public Review Review { get; set; } = default!;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
