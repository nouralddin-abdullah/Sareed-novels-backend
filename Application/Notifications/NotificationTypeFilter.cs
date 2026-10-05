using Domain.Constants;

namespace Application.Notifications;

/// <summary>
/// The <c>types</c> filter of GET /api/notifications and GET /api/notifications/unread-count (#78): NotificationType
/// names, comma-separated, matched whatever their letter case and the spaces around them. Unknown names are ignored, so
/// an app that names a type this server doesn't have still gets the ones it has; a filter that names no known type (or
/// is empty, or missing) filters nothing: every type.
/// </summary>
public static class NotificationTypeFilter
{
    private static readonly Dictionary<string, string> Known =
        NotificationType.All.ToDictionary(type => type, StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// The types <paramref name="values"/> name, each once and spelled as <see cref="NotificationType"/> spells it; each
    /// value is a comma-separated list (the parameter may also be repeated). Null when they name no known type: every type.
    /// </summary>
    public static IReadOnlyList<string>? Parse(IEnumerable<string?>? values)
    {
        var types = (values ?? [])
            .SelectMany(value => (value ?? "").Split(','))
            .Select(name => Known.GetValueOrDefault(name.Trim()))
            .OfType<string>()
            .Distinct()
            .ToList();
        return types.Count == 0 ? null : types;
    }
}
