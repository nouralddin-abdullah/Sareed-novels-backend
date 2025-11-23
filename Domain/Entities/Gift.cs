namespace Domain.Entities;

public class Gift
{
    public Guid Id { get; set; }
    public string Name { get; set; } = default!;
    public string ImageUrl { get; set; } = default!;
    public decimal Cost { get; set; } // Cost in points
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }
    
    // Navigation
    public ICollection<GiftTransaction> Transactions { get; set; } = new List<GiftTransaction>();
}
