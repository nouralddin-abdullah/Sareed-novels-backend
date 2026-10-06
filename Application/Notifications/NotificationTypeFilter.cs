using Application.Common;
using Domain.Constants;

namespace Application.Notifications;

/// <summary>
/// The <c>types</c> filter of GET /api/notifications and GET /api/notifications/unread-count (#78), and of PATCH
/// /api/notifications/read (#92): NotificationType names, comma-separated, matched whatever their letter case and the
/// spaces around them (<see cref="NameFilter"/>). Unknown names are ignored, so an app that names a type this server
/// doesn't have still gets the ones it has. For a list, a filter that names no known type (or is empty, or missing)
/// filters nothing: every type. For marking read, it marks nothing.
/// </summary>
public static class NotificationTypeFilter
{
    private static readonly NameFilter Types = new(NotificationType.All);

    /// <summary>
    /// The types <paramref name="values"/> name, each once and spelled as <see cref="NotificationType"/> spells it; each
    /// value is a comma-separated list (the parameter may also be repeated). Null when they name no known type: every type.
    /// </summary>
    public static IReadOnlyList<string>? Parse(IEnumerable<string?>? values) => Types.ForList(values);

    /// <summary>
    /// For marking read (#92), which must touch only the types named: the known types <paramref name="values"/> name, as
    /// <see cref="Parse"/> reads them, but never every type: empty when they name none.
    /// </summary>
    public static IReadOnlyList<string> Exactly(IEnumerable<string?>? values) => Types.Known(values);
}
