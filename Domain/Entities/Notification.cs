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

    /// <summary>
    /// A GiftReceived notification's gift and how many were sent (#25), which the message only names in words; null for
    /// other types (and gift notifications from before). No foreign key: gifts are retired, not deleted.
    /// </summary>
    public Guid? GiftId { get; set; }
    public int? GiftCount { get; set; }

    /// <summary>
    /// A GiftReceived notification's <see cref="GiftTransaction"/> (#31), where its message is read from, so that a
    /// moderator removing the message removes it here too; null for other types and gift notifications from before.
    /// No foreign key, like <see cref="GiftId"/>.
    /// </summary>
    public Guid? GiftTransactionId { get; set; }

    public void MarkAsRead()
    {
        IsRead = true;
    }
}
