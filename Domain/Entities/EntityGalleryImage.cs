namespace Domain.Entities;

public class EntityGalleryImage
{
    public Guid Id { get; set; }
    public Guid EntityId { get; set; }
    public NovelEntity Entity { get; set; } = default!;
    
    public string ImageUrl { get; set; } = default!;
    public string? Caption { get; set; }
    public int OrderIndex { get; set; }
    
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
