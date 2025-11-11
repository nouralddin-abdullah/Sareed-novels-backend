namespace Application.Entities.Validators;

/// <summary>
/// Icon validator based on frontend lucide-react icons
/// Valid icons: users, map-pin, shield, flag, book, sparkles, swords, globe
/// </summary>
public static class EntityIconValidator
{
    private static readonly HashSet<string> ValidIcons = new(StringComparer.OrdinalIgnoreCase)
    {
        "users",      // ?????? (Characters)
        "map-pin",    // ????? (Locations)
        "mappin",     // Alternative format
        "shield",     // ???? / ????? (Shields/Items)
        "flag",       // ????? (Factions)
        "book",       // ??? (Books)
        "sparkles",   // ??? (Magic)
        "swords",     // ????? (Weapons)
        "globe"       // ????? (Worlds)
    };

    public const string DefaultIcon = "users";

    public static bool IsValid(string? icon)
    {
        if (string.IsNullOrWhiteSpace(icon))
            return true; // Icon is optional

        return ValidIcons.Contains(icon);
    }

    public static string? Normalize(string? icon)
    {
        if (string.IsNullOrWhiteSpace(icon))
            return null;

        // Convert to lowercase and handle alternative formats
        var normalized = icon.ToLowerInvariant().Replace("_", "-");

        return IsValid(normalized) ? normalized : null;
    }

    public static List<string> GetValidIcons()
    {
        return new List<string>
        {
            "users",
            "map-pin",
            "shield",
            "flag",
            "book",
            "sparkles",
            "swords",
            "globe"
        };
    }
}
