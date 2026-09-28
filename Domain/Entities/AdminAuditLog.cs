using Domain.Moderation;

namespace Domain.Entities;

/// <summary>
/// One admin action, for the record: which admin did what to which user, why and when. It holds ids and the admin's
/// own words only, nothing copied from the account acted on, so it keeps no personal data of a member it outlives. It
/// is written in the same transaction as the action, so there is a row exactly when the action happened.
/// </summary>
public class AdminAuditLog
{
    public const int NoteMaxLength = 500;

    public Guid Id { get; set; }

    /// <summary>The admin's user id. No foreign key, so the record outlives the admin's account (as Report.ResolvedById).</summary>
    public string AdminId { get; set; } = default!;

    public AdminAuditAction Action { get; set; }

    /// <summary>The user acted on. No foreign key either.</summary>
    public string TargetUserId { get; set; } = default!;

    /// <summary>
    /// Why, by a stable name (for <see cref="AdminAuditAction.DeleteAccount"/>, an <see cref="AccountDeletionReason"/>);
    /// null for an action without one.
    /// </summary>
    public string? Reason { get; set; }

    /// <summary>The admin's own note, optional.</summary>
    public string? Note { get; set; }

    public DateTime CreatedAt { get; set; }
}
