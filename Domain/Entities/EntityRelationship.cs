namespace Domain.Entities;

public class EntityRelationship
{
    public Guid Id { get; set; }
    
    public Guid SourceEntityId { get; set; }
    public NovelEntity SourceEntity { get; set; } = default!;
    
    public Guid TargetEntityId { get; set; }
    public NovelEntity TargetEntity { get; set; } = default!;
    
    // User-defined relationship type: "ally", "enemy", "family", "mentor", "love", etc.
    public string RelationType { get; set; } = default!;
    
    // Optional label shown in UI: "Best Friend", "Father of", "Sworn Enemy", etc.
    public string? Label { get; set; }
    
    // Reverse label for bidirectional relationships: "Son", "Student", "Enemy", etc.
    public string? ReverseLabel { get; set; }
    
    public string? Description { get; set; }
    
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public bool IsDeleted { get; set; } = false;
    
    public void MarkAsDeleted()
    {
        IsDeleted = true;
    }
}
