namespace Domain.Entities;

public class SearchIndexOutbox
{
    public Guid Id { get; set; }
    public string EntityType { get; set; } = default!; // "Novel"
    public Guid EntityId { get; set; }
    public string Action { get; set; } = default!; // "Index", "Update", "Delete"
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public bool Processed { get; set; } = false;
    public int RetryCount { get; set; } = 0;
    public DateTime? ProcessedAt { get; set; }
    public string? ErrorMessage { get; set; }
}
