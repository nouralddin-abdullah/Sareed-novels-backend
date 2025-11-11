namespace Application.Search.DTOs;

public class NovelEntitySearchDocument
{
    public string Id { get; set; } = default!;
    public string NovelId { get; set; } = default!;
    public string Section { get; set; } = default!;
    public string? Icon { get; set; }
    public string Name { get; set; } = default!;
    public string? ShortDescription { get; set; }
    public string? Description { get; set; }
    public string? Role { get; set; }
    public string? ImageUrl { get; set; }
}

public class EntityArticleDoc
{
    public string Id { get; set; } = default!;
    public string Title { get; set; } = default!;
    public string Content { get; set; } = default!;
    public int OrderIndex { get; set; }
}

public class EntityRelationshipDoc
{
    public string Id { get; set; } = default!;
    public string TargetEntityId { get; set; } = default!;
    public string TargetEntityName { get; set; } = default!;
    public string RelationType { get; set; } = default!;
    public string? Label { get; set; }
}
