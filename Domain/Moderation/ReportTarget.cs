namespace Domain.Moderation;

/// <summary>A reported item as it is now: whose it is (the reported user, for a user) and a short excerpt of its text.</summary>
public sealed record ReportTarget(string OwnerId, string? Excerpt);

/// <summary>
/// A reported item as the moderators' list shows it. <see cref="Exists"/> is false once it was deleted (a report
/// outlives its target); the other fields then are null and the list falls back to what the report stored.
/// </summary>
/// <param name="Link">Where the web app shows it (e.g. "/novel/{slug}/chapter/{id}"), when that can be told.</param>
public sealed record ReportTargetState(bool Exists, string? OwnerId, string? Excerpt, string? Link)
{
    public static readonly ReportTargetState Gone = new(false, null, null, null);
}

/// <summary>Whether a signed-in viewer and another user have blocked each other (either way).</summary>
public readonly record struct BlockRelation(bool ViewerBlockedOther, bool OtherBlockedViewer)
{
    public bool Either => ViewerBlockedOther || OtherBlockedViewer;
}

/// <summary>A user someone blocked, with their current names (for the blocked-users list).</summary>
public sealed record BlockedUser(string UserId, string UserName, string DisplayName, string? ProfilePhoto, DateTime BlockedAt);
