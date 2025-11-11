namespace Application.Entities.DTOs;

public class EntityDTO
{
    public Guid Id { get; set; }
    public Guid NovelId { get; set; }
    public string Section { get; set; } = default!;
    public string? Icon { get; set; }
    public string Name { get; set; } = default!;
    public string? ShortDescription { get; set; }
    public string? Description { get; set; }
    public string? Role { get; set; }
    public string? ImageUrl { get; set; }
    public Dictionary<string, object> Attributes { get; set; } = new();
    public List<EntityArticleDTO> Articles { get; set; } = new();
    public List<EntityGalleryImageDTO> GalleryImages { get; set; } = new();
    public List<EntityRelationshipDTO> Relationships { get; set; } = new();
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public bool IsOwner { get; set; }
}

public class EntityArticleDTO
{
    public Guid Id { get; set; }
    public string Title { get; set; } = default!;
    public string Content { get; set; } = default!;
    public int OrderIndex { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class EntityGalleryImageDTO
{
    public Guid Id { get; set; }
    public string ImageUrl { get; set; } = default!;
    public string? Caption { get; set; }
    public int OrderIndex { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class EntityRelationshipDTO
{
    public Guid Id { get; set; }
    public Guid TargetEntityId { get; set; }
    public string TargetEntityName { get; set; } = default!;
    public string? TargetEntityImage { get; set; }
    public string RelationType { get; set; } = default!;
    public string? Label { get; set; }
    public string? ReverseLabel { get; set; }
    public string? Description { get; set; }
}

public class EntityListDTO
{
    public Guid Id { get; set; }
    public string Section { get; set; } = default!;
    public string? Icon { get; set; }
    public string Name { get; set; } = default!;
    public string? ShortDescription { get; set; }
    public string? ImageUrl { get; set; }
    public DateTime CreatedAt { get; set; }
    public int ArticlesCount { get; set; }
    public int RelationshipsCount { get; set; }
}

public class EntityTypeStatsDTO
{
    public string Section { get; set; } = default!;
    public int Count { get; set; }
    public List<string> AttributeKeys { get; set; } = new();
}
