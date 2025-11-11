namespace Domain.Entities;

public class EntityArticle
{
    public Guid Id { get; set; }
    public Guid EntityId { get; set; }
    public NovelEntity Entity { get; set; } = default!;
    
    public string Title { get; set; } = default!;
    public string Content { get; set; } = default!;
    
    // For ordering multiple articles (e.g., Backstory 1, Backstory 2, etc.)
    public int OrderIndex { get; set; }
    
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public bool IsDeleted { get; set; } = false;
    
    public void MarkAsDeleted()
    {
        IsDeleted = true;
        UpdatedAt = DateTime.UtcNow;
    }
}
