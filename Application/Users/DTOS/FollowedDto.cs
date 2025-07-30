namespace Application.Users.DTOS;

public class FollowedDto
{
    public string UserId { get; set; } = default!;
    public string UserName { get; set; } = default!;
    public string DisplayName { get; set; } = default!;
    public string? ProfilePhoto { get; set; }
}
