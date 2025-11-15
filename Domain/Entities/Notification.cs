namespace Domain.Entities;

public class Notification
{
    public Guid Id { get; set; }
    public string UserId { get; set; } = default!;
    public User User { get; set; } = default!;
    public string Type { get; set; } = default!;
    public string ActorId { get; set; } = default!;
    public string ActorDisplayName { get; set; } = default!;
    public string? ActorProfilePhoto { get; set; }
    public string Message { get; set; } = default!;
    public string ActionUrl { get; set; } = default!;
    public bool IsRead { get; set; } = false;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    
    // Optional: For grouping/context (Phase 2)
    public Guid? RelatedEntityId { get; set; }
    public string? RelatedEntityType { get; set; }
    
    public void MarkAsRead()
    {
        IsRead = true;
    }
}
