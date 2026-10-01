using Domain.Profiles;

namespace Application.Users.DTOS;

/// <summary>
/// Who may browse the signed-in member's lists on their profile (#61): GET and PATCH /api/User/me/privacy. Each is
/// "Everyone" (the default) or "OnlyMe", always spelled so.
/// </summary>
public class ListPrivacyDto
{
    /// <summary>Who may browse their reviews (GET /api/User/{userName}/reviews).</summary>
    public string Reviews { get; set; } = default!;

    /// <summary>Who may browse their comments (GET /api/User/{userName}/comments).</summary>
    public string Comments { get; set; } = default!;

    public static ListPrivacyDto From(ProfileListPrivacy privacy) => new()
    {
        Reviews = privacy.Reviews.ToString(),
        Comments = privacy.Comments.ToString()
    };
}
