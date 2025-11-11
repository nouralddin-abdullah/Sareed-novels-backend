namespace Domain.Entities;

public class NovelEntity
{
    public Guid Id { get; set; }
    public Guid NovelId { get; set; }
    public Novel Novel { get; set; } = default!;
    
    // Section (e.g., "Characters", "Locations", "Weapons") - replaces EntityType
    public string Section { get; set; } = default!;
    
    // Icon identifier for the section (e.g., "users", "map-pin", "swords")
    public string? Icon { get; set; }
    
    // Entity name
    public string Name { get; set; } = default!;
    
    // Short description (e.g., "????? ??????? ????? ????? ??????? ?? ?????? ?????? ???????")
    public string? ShortDescription { get; set; }
    
    // Full description
    public string? Description { get; set; }
    
    // Role/Title (e.g., "Main Protagonist", "Antagonist", "Support Character")
    public string? Role { get; set; }
    
    // Main profile image
    public string? ImageUrl { get; set; }
    
    // Flexible JSON storage for any custom attributes
    // Example: {"age": 25, "power": "Fire Magic", "rank": "S-Class"}
    public string AttributesJson { get; set; } = "{}";
    
    // Navigation properties
    public List<EntityArticle> Articles { get; set; } = new();
    public List<EntityGalleryImage> GalleryImages { get; set; } = new();
    public List<EntityRelationship> SourceRelationships { get; set; } = new();
    public List<EntityRelationship> TargetRelationships { get; set; } = new();
    
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public bool IsDeleted { get; set; } = false;
    
    public void MarkAsDeleted()
    {
        IsDeleted = true;
        UpdatedAt = DateTime.UtcNow;
    }
}
