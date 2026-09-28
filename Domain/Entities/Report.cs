using Domain.Moderation;

namespace Domain.Entities;

/// <summary>
/// A user's report of content (or of another user) for the moderators. A reporter has at most one open report per
/// target; an admin's action on a report (dismiss, remove the content, suspend its author) closes every open report on
/// the same target.
/// </summary>
public class Report
{
    public const int DetailsMaxLength = 1000;
    public const int ExcerptMaxLength = 500;

    public Guid Id { get; set; }
    public string ReporterId { get; set; } = default!;
    public ReportTargetType TargetType { get; set; }
    /// <summary>The reported item's id; for <see cref="ReportTargetType.User"/>, the user's id.</summary>
    public Guid TargetId { get; set; }
    /// <summary>
    /// Whose the reported item was when it was reported (the reported user, for a user): who SuspendUser suspends,
    /// even after the item is deleted. No foreign key: reports outlive what they are about.
    /// </summary>
    public string TargetOwnerId { get; set; } = default!;
    /// <summary>The reported text as it was (a comment's text, a novel's title...), so moderators can still read it once it's deleted.</summary>
    public string? TargetExcerpt { get; set; }
    public ReportReason Reason { get; set; }
    public string? Details { get; set; }
    public ReportStatus Status { get; set; } = ReportStatus.Open;
    public DateTime CreatedAt { get; set; }
    public DateTime? ResolvedAt { get; set; }
    /// <summary>The admin who closed it. No foreign key, so removing an admin account keeps the history.</summary>
    public string? ResolvedById { get; set; }
    public ReportAction? Resolution { get; set; }
}
