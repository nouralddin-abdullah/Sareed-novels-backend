using MediatR;

namespace Application.Notifications.Commands.MarkTypesAsRead;

/// <summary>
/// Marks the caller's unread notifications of some types read, and no others (#92: «على رواياتي»'s «تحديد الكل
/// كمقروء»).
/// </summary>
/// <param name="types">The types as sent: comma-separated NotificationType names (<see cref="NotificationTypeFilter"/>).</param>
public class MarkTypesAsReadCommand(IReadOnlyList<string>? types) : IRequest<MarkTypesAsReadResult>
{
    /// <summary>The code of a request whose types name nothing at all (400).</summary>
    public const string ValidationFailed = "ValidationFailed";

    public const string TypesRequiredMessage = "حدّد أنواع الإشعارات التي تريد تحديدها كمقروءة";

    public IReadOnlyList<string>? Types { get; } = types;
}

/// <summary>What marking read did, and the unread counts it left.</summary>
public class MarkTypesAsReadResult
{
    /// <summary>How many of the caller's notifications it marked read (0 when none of those types was unread).</summary>
    public int Marked { get; set; }

    /// <summary>The caller's unread notifications of every type after it (the bell), as GET unread-count answers.</summary>
    public int UnreadCount { get; set; }

    /// <summary>
    /// Their unread notifications of the types marked after it, as GET unread-count with the same types answers: 0, unless
    /// one arrived meanwhile.
    /// </summary>
    public int TypesUnreadCount { get; set; }
}
