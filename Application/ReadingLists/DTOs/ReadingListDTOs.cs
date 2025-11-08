namespace Application.ReadingLists.DTOs;

public class ReadingListPreviewDTO
{
    public Guid Id { get; set; }
    public string Name { get; set; } = default!;
    public string? Description { get; set; }
    public string? CoverImageUrl { get; set; }
    public bool IsPublic { get; set; }
    public int NovelsCount { get; set; }
    public int FollowersCount { get; set; }
    public DateTime UpdatedAt { get; set; }
    public List<NovelPreviewDTO> PreviewNovels { get; set; } = new();
    public bool IsOwner { get; set; }
    public bool IsFollowing { get; set; }
}

public class NovelPreviewDTO
{
    public Guid NovelId { get; set; }
    public string Slug { get; set; } = default!;
    public string CoverImageUrl { get; set; } = default!;
    public string Title { get; set; } = default!;
}

public class ReadingListDetailDTO
{
    public Guid Id { get; set; }
    public string Name { get; set; } = default!;
    public string? Description { get; set; }
    public string? CoverImageUrl { get; set; }
    public bool IsPublic { get; set; }
    public int NovelsCount { get; set; }
    public int FollowersCount { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public string OwnerUserId { get; set; } = default!;
    public string OwnerUserName { get; set; } = default!;
    public string OwnerDisplayName { get; set; } = default!;
    public string? OwnerProfilePhoto { get; set; }
    public List<NovelInListDTO> Novels { get; set; } = new();
    public bool IsOwner { get; set; }
    public bool IsFollowing { get; set; }
}

public class NovelInListDTO
{
    public Guid NovelId { get; set; }
    public string Title { get; set; } = default!;
    public string Slug { get; set; } = default!;
    public string CoverImageUrl { get; set; } = default!;
    public string Summary { get; set; } = default!;
    public decimal TotalAverageScore { get; set; }
    public int ReviewCount { get; set; }
    public List<string> Genres { get; set; } = new();
    public int OrderIndex { get; set; }
    public DateTime AddedAt { get; set; }
}
