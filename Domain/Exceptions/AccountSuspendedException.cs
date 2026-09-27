using System.Globalization;
using Domain.Moderation;

namespace Domain.Exceptions;

/// <summary>
/// Sign-in refused because a moderator suspended the account. HTTP 403 with JSON
/// <c>{"code": "AccountSuspended", "message", "suspendedUntil", "permanent"}</c> (suspendedUntil is null when permanent).
/// </summary>
public sealed class AccountSuspendedException(DateTime suspendedUntil)
    : ForbidException(MessageFor(suspendedUntil), ErrorCode)
{
    public const string ErrorCode = "AccountSuspended";

    /// <summary>When the suspension ends (UTC); <see cref="Suspension.Permanent"/> when it doesn't.</summary>
    public DateTime SuspendedUntil { get; } = DateTime.SpecifyKind(suspendedUntil, DateTimeKind.Utc);

    public bool Permanent => Suspension.IsPermanent(SuspendedUntil);

    private const string Contact = "للاستفسار راسلنا على support@sardnovels.com";

    public static string MessageFor(DateTime suspendedUntil) => Suspension.IsPermanent(suspendedUntil)
        ? $"تم إيقاف حسابك نهائياً لمخالفته قواعد سرد. {Contact}"
        : $"تم إيقاف حسابك مؤقتاً حتى {suspendedUntil.ToString("yyyy/MM/dd", CultureInfo.InvariantCulture)} لمخالفته قواعد سرد. {Contact}";
}
