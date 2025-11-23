namespace Domain.Entities;

public class GiftTransaction
{
    public Guid Id { get; set; }
    public Guid GiftId { get; set; }
    public Gift Gift { get; set; } = default!;
    public Guid NovelId { get; set; }
    public Novel Novel { get; set; } = default!;
    public string SenderId { get; set; } = default!;
    public User Sender { get; set; } = default!;
    public int Count { get; set; } = 1; // x1, x2, x3, etc.
    public decimal TotalCost { get; set; } // Cost * Count
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
