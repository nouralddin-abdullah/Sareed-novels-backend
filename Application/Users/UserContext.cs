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
        return new CurrentUser(userId, email, userName, DisplayName);
    }
}
