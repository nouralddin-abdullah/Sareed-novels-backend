namespace Application.Common;

/// <summary>
/// A query filter that names some of a fixed set of names: the notification types of GET /api/notifications (#78) and
/// PATCH /api/notifications/read, and the transaction types of GET /api/wallet/transactions (#92). Names come in
/// comma-separated lists (the parameter may also be repeated) and match whatever their letter case and the spaces
/// around them. Unknown names are ignored, so an app that names one this server doesn't have still gets the ones it has.
/// </summary>
public sealed class NameFilter(IEnumerable<string> names)
{
    private readonly Dictionary<string, string> known = names.ToDictionary(name => name, StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// The known names <paramref name="values"/> name, each once and spelled as the set spells it; empty when they name
    /// none. What a write must keep to: it never widens to every name.
    /// </summary>
    public IReadOnlyList<string> Known(IEnumerable<string?>? values) =>
        Named(values).Select(name => known.GetValueOrDefault(name)).OfType<string>().Distinct().ToList();

    /// <summary>
    /// What a list filters by: the known names <paramref name="values"/> name, or null when they name none (or nothing at
    /// all), which filters nothing: every name.
    /// </summary>
    public IReadOnlyList<string>? ForList(IEnumerable<string?>? values) => Known(values) is { Count: > 0 } names ? names : null;

    /// <summary>Whether <paramref name="values"/> name anything, known or not: not missing, empty, or only commas and spaces.</summary>
    public static bool NamesAny(IEnumerable<string?>? values) => Named(values).Any();

    private static IEnumerable<string> Named(IEnumerable<string?>? values) =>
        (values ?? []).SelectMany(value => (value ?? "").Split(',')).Select(name => name.Trim()).Where(name => name.Length > 0);
}
