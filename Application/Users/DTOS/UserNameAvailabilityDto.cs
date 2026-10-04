namespace Application.Users.DTOS;

/// <summary>
/// GET /api/User/username-available (#69): whether the signed-in member could take a user name now, and if not, why:
/// what PATCH /api/User/update-me would refuse it with (<see cref="UserNameCheck"/>).
/// </summary>
/// <param name="Available">
/// True when update-me would take the name: it meets the rules and no other account holds it. The member's own name,
/// in any letter case, is available to them.
/// </param>
/// <param name="Code">Null when available; else InvalidUserName, UserNameTaken or ReservedUserName.</param>
/// <param name="Message">Arabic: update-me's message for the refusal, or <see cref="AvailableMessage"/>.</param>
public sealed record UserNameAvailabilityDto(bool Available, string? Code, string Message)
{
    public const string AvailableMessage = "اسم المستخدم متاح";

    public static UserNameAvailabilityDto From(UserNameRefusal? refusal) =>
        refusal is null ? new(true, null, AvailableMessage) : new(false, refusal.Code, refusal.Message);
}
