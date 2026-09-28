using Domain.Entities;
using Domain.Moderation;

namespace Application.Reports;

/// <summary>A report as its reporter gets it back. Enum values are their names ("Comment", "Spam", "Open").</summary>
public class ReportDto
{
    public Guid Id { get; set; }
    public string TargetType { get; set; } = default!;
    public Guid TargetId { get; set; }
    public string Reason { get; set; } = default!;
    public string? Details { get; set; }
    public string Status { get; set; } = default!;
    public DateTime CreatedAt { get; set; }

    public static ReportDto From(Report report) => new()
    {
        Id = report.Id,
        TargetType = report.TargetType.ToString(),
        TargetId = report.TargetId,
        Reason = report.Reason.ToString(),
        Details = report.Details,
        Status = report.Status.ToString(),
        CreatedAt = Utc.Of(report.CreatedAt)
    };
}

/// <summary>A user as the moderators' list names them.</summary>
public class ReportUserDto
{
    public string UserId { get; set; } = default!;
    public string UserName { get; set; } = default!;
    public string DisplayName { get; set; } = default!;
    public string? ProfilePhoto { get; set; }
}

/// <summary>Whose the reported item is, and whether they're suspended now.</summary>
public class ReportOwnerDto : ReportUserDto
{
    public bool IsSuspended { get; set; }
    /// <summary>When the suspension ends (UTC); null when not suspended or suspended for good.</summary>
    public DateTime? SuspendedUntil { get; set; }
    public bool SuspendedPermanently { get; set; }
}

/// <summary>The reported item: still there or deleted since, with what it said when it was reported.</summary>
public class AdminReportTargetDto
{
    public string Type { get; set; } = default!;
    public Guid Id { get; set; }
    /// <summary>True once the item is gone (deleted by its author or a moderator); the report still lists.</summary>
    public bool IsDeleted { get; set; }
    /// <summary>Its text when it was reported (a comment's text, a novel's title, a user's names...).</summary>
    public string? Excerpt { get; set; }
    /// <summary>Its text now; null once deleted.</summary>
    public string? CurrentExcerpt { get; set; }
    /// <summary>Where the web app shows it; null once deleted.</summary>
    public string? Link { get; set; }
    /// <summary>Its author (the user, for a user); null when that account no longer exists.</summary>
    public ReportOwnerDto? Owner { get; set; }
}

/// <summary>A report in the moderators' list.</summary>
public class AdminReportDto
{
    public Guid Id { get; set; }
    public string Reason { get; set; } = default!;
    public string? Details { get; set; }
    public string Status { get; set; } = default!;
    public DateTime CreatedAt { get; set; }
    /// <summary>Null when the reporter's account no longer exists.</summary>
    public ReportUserDto? Reporter { get; set; }
    public AdminReportTargetDto Target { get; set; } = default!;
    /// <summary>Open reports on the same target, this one included: an action closes them all.</summary>
    public int OpenReportsOnTarget { get; set; }
    /// <summary>What closed it (Dismiss, RemoveContent, SuspendUser); null while open.</summary>
    public string? Action { get; set; }
    public DateTime? ResolvedAt { get; set; }
    public string? ResolvedById { get; set; }
}

/// <summary>What an admin's action on a report did.</summary>
public class ResolveReportResult
{
    public Guid ReportId { get; set; }
    public string Action { get; set; } = default!;
    /// <summary>Open reports on the target this action closed (0 when they were all closed already).</summary>
    public int ResolvedReports { get; set; }
    /// <summary>RemoveContent: true when this action deleted it, false when it was gone already.</summary>
    public bool ContentRemoved { get; set; }
    /// <summary>SuspendUser: who was suspended, until when (null when for good) and whether for good.</summary>
    public string? SuspendedUserId { get; set; }
    public DateTime? SuspendedUntil { get; set; }
    public bool SuspendedPermanently { get; set; }
}

internal static class Utc
{
    /// <summary>Dates come back from SQL Server without a kind; they are UTC, and say so in JSON ("Z").</summary>
    public static DateTime Of(DateTime value) => DateTime.SpecifyKind(value, DateTimeKind.Utc);

    public static DateTime? Of(DateTime? value) => value is { } v ? Of(v) : null;

    /// <summary>The suspension end to show: null when not suspended now or suspended for good.</summary>
    public static DateTime? SuspensionEnd(DateTime? suspendedUntil, DateTime utcNow) =>
        Suspension.IsActive(suspendedUntil, utcNow) && !Suspension.IsPermanent(suspendedUntil!.Value) ? Of(suspendedUntil) : null;
}
