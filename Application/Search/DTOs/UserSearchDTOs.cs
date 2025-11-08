namespace Application.Search.DTOs;

public class SearchUsersRequest
{
    public string? Query { get; set; }
    
    // Pagination
    public int PageNumber { get; set; } = 1;
    public int PageSize { get; set; } = 20;
}

public class UserSearchResult
{
    public string Id { get; set; } = default!;
    public string UserName { get; set; } = default!;
    public string DisplayName { get; set; } = default!;
    public string? ProfilePhoto { get; set; }
    public int FollowersCount { get; set; }
    public int FollowingCount { get; set; }
    public int NovelsCount { get; set; }
}
