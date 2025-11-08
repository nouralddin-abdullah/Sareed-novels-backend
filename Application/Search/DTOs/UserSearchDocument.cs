namespace Application.Search.DTOs;

/// <summary>
/// Elasticsearch document for user search
/// </summary>
public class UserSearchDocument
{
    public string Id { get; set; } = default!;
    public string UserName { get; set; } = default!;
    public string DisplayName { get; set; } = default!;
    public string? ProfilePhoto { get; set; }
    public int FollowersCount { get; set; }
    public int FollowingCount { get; set; }
    public int NovelsCount { get; set; }
    public DateTime CreatedAt { get; set; }
}
