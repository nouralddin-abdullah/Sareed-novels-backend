namespace Application.Users;

public record CurrentUser(string Id, string Email, string UserName, string DisplayName)
{
    /// <summary>When the request's access token was issued (its "iat", UTC); null for tokens from before it was added.</summary>
    public DateTime? TokenIssuedAt { get; init; }
}
