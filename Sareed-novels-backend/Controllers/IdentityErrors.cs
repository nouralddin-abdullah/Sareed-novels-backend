using Microsoft.AspNetCore.Identity;

namespace Sareed_novels_backend.Controllers;

/// <summary>A refused ASP.NET Identity operation (a password change or reset, an email confirmation) as an API error.</summary>
internal static class IdentityErrors
{
    /// <summary>
    /// <c>{code, message, succeeded: false, errors}</c>: the first problem's Identity code (PasswordMismatch,
    /// InvalidToken, PasswordTooShort...) and description (Arabic, from ArabicIdentityErrorDescriber), and all of them
    /// in errors ([{code, description}], as the IdentityResult these endpoints used to return).
    /// </summary>
    public static object Body(IdentityResult result)
    {
        var first = result.Errors.FirstOrDefault();
        return new
        {
            code = first?.Code ?? "OperationFailed",
            message = first?.Description ?? "تعذّر إتمام العملية. حاول مرة أخرى.",
            succeeded = false,
            errors = result.Errors
        };
    }
}
