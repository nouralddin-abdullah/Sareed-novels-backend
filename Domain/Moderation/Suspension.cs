namespace Domain.Moderation;

/// <summary>
/// An account suspended by a moderator (<see cref="Entities.User.SuspendedUntil"/>) can't sign in and its access
/// tokens are refused until the suspension ends or an admin lifts it.
/// </summary>
public static class Suspension
{
    /// <summary>What <see cref="Entities.User.SuspendedUntil"/> holds for a suspension without an end.</summary>
    public static readonly DateTime Permanent = DateTime.MaxValue;

    /// <summary>Longest suspension an admin can give in days; longer ones are permanent.</summary>
    public const int MaxDays = 3650;

    public static bool IsPermanent(DateTime until) => until.Year >= Permanent.Year;

    /// <summary>Whether a suspension ending at <paramref name="until"/> (UTC) is still on at <paramref name="utcNow"/>.</summary>
    public static bool IsActive(DateTime? until, DateTime utcNow) => until is { } end && end > utcNow;

    /// <summary>When a suspension of <paramref name="days"/> days given at <paramref name="utcNow"/> ends; null days is permanent.</summary>
    public static DateTime Until(int? days, DateTime utcNow) => days is { } d ? utcNow.AddDays(d) : Permanent;
}
