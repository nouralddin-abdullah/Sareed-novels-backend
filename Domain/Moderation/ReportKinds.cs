namespace Domain.Moderation;

// The names below are the API's contract with the web and the mobile app, and are stored as text: never rename one.

/// <summary>What a report is about.</summary>
public enum ReportTargetType
{
    Comment,
    Review,
    Post,
    User,
    Novel,
    ReadingList
}

/// <summary>Why it was reported.</summary>
public enum ReportReason
{
    Spam,
    Harassment,
    Sexual,
    Violence,
    HateSpeech,
    Spoiler,
    Other
}

/// <summary>
/// <see cref="Open"/> until an admin acts: <see cref="Dismissed"/> when nothing was wrong, <see cref="Resolved"/> when
/// the content was removed or its author suspended.
/// </summary>
public enum ReportStatus
{
    Open,
    Resolved,
    Dismissed
}

/// <summary>What an admin did about a report, and with it every other open report on the same target.</summary>
public enum ReportAction
{
    Dismiss,
    RemoveContent,
    SuspendUser
}

public static class EnumNames
{
    /// <summary>
    /// One of <typeparamref name="T"/>'s names, ignoring case. Unlike <see cref="Enum.TryParse{TEnum}(string?, bool, out TEnum)"/>,
    /// numbers ("3") and undefined names are refused.
    /// </summary>
    public static bool TryParse<T>(string? value, out T result) where T : struct, Enum
    {
        var trimmed = value?.Trim();
        foreach (var name in Enum.GetNames<T>())
        {
            if (string.Equals(name, trimmed, StringComparison.OrdinalIgnoreCase))
            {
                result = Enum.Parse<T>(name);
                return true;
            }
        }
        result = default;
        return false;
    }

    /// <summary>The names, for messages and docs: "Spam|Harassment|...".</summary>
    public static string List<T>() where T : struct, Enum => string.Join("|", Enum.GetNames<T>());
}
