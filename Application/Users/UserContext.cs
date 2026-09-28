using System.Globalization;
using System.Security.Claims;
using Microsoft.AspNetCore.Http;

namespace Application.Users;

public interface IUserContext
{
    CurrentUser? GetCurrentUser();
}
public class UserContext(IHttpContextAccessor httpContextAccessor) : IUserContext
{
    public CurrentUser? GetCurrentUser()
    {
        var user = (httpContextAccessor?.HttpContext?.User) ?? throw new InvalidOperationException("User Context is not present");

        if (user.Identity == null || !user.Identity.IsAuthenticated)
        {
            return null;
        }

        var userId = user.FindFirst(c => c.Type == ClaimTypes.NameIdentifier)!.Value;
        var email = user.FindFirst(c => c.Type == ClaimTypes.Email)!.Value;
        var userName = user.FindFirst(c => c.Type == ClaimTypes.Name)!.Value;
        var DisplayName = user.FindFirst(c => c.Type == "DisplayName")!.Value;
        return new CurrentUser(userId, email, userName, DisplayName) { TokenIssuedAt = IssuedAt(user) };
    }

    /// <summary>The token's "iat" (seconds since 1970, UTC), which the JWT bearer handler passes on as a claim.</summary>
    private static DateTime? IssuedAt(ClaimsPrincipal user) =>
        long.TryParse(user.FindFirst("iat")?.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var seconds)
        && seconds is >= 0 and <= MaxUnixSeconds
            ? DateTimeOffset.FromUnixTimeSeconds(seconds).UtcDateTime
            : null;

    // 9999-12-31T23:59:59Z, the last second DateTimeOffset can hold.
    private const long MaxUnixSeconds = 253402300799;
}
