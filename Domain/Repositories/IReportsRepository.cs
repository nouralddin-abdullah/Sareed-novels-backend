using Domain.Entities;
using Domain.Moderation;

namespace Domain.Repositories;

/// <summary>Reports of content and users for the moderators (<see cref="Report"/>).</summary>
public interface IReportsRepository
{
    /// <summary>
    /// The item as it is now (its owner and a text excerpt) when it exists and <paramref name="viewerId"/> can see it:
    /// comments and posts not deleted, novels not deleted or drafts, reading lists public (or the viewer's own), gift
    /// messages not removed.
    /// </summary>
    Task<ReportTarget?> FindTargetAsync(ReportTargetType type, Guid targetId, string viewerId, CancellationToken cancellationToken = default);

    /// <summary>The reporter's open report on this target, if any.</summary>
    Task<Report?> GetOpenReportAsync(string reporterId, ReportTargetType type, Guid targetId, CancellationToken cancellationToken = default);

    /// <summary>How many reports the user made since <paramref name="since"/> (for the per-user limit).</summary>
    Task<int> CountCreatedSinceAsync(string reporterId, DateTime since, CancellationToken cancellationToken = default);

    /// <summary>
    /// Saves a new open report. If the same reporter got an open report on the same target in first (a concurrent
    /// duplicate), nothing is saved and that report is returned with Created false.
    /// </summary>
    Task<(Report Report, bool Created)> AddAsync(Report report, CancellationToken cancellationToken = default);

    Task<Report?> GetByIdAsync(Guid reportId, CancellationToken cancellationToken = default);

    /// <summary>A page of reports, oldest first when listing open ones (the queue), newest first otherwise; all statuses when null.</summary>
    Task<(IReadOnlyList<Report> Reports, int TotalCount)> GetPageAsync(ReportStatus? status, int pageNumber, int pageSize, CancellationToken cancellationToken = default);

    /// <summary>Each target as it is now; <see cref="ReportTargetState.Gone"/> for one deleted since. A fixed number of queries.</summary>
    Task<Dictionary<(ReportTargetType Type, Guid Id), ReportTargetState>> DescribeTargetsAsync(
        IReadOnlyCollection<(ReportTargetType Type, Guid Id)> targets, CancellationToken cancellationToken = default);

    /// <summary>How many open reports each target has (targets without any are left out).</summary>
    Task<Dictionary<(ReportTargetType Type, Guid Id), int>> CountOpenReportsAsync(
        IReadOnlyCollection<(ReportTargetType Type, Guid Id)> targets, CancellationToken cancellationToken = default);

    /// <summary>Closes every open report on the target with the admin's action, in one UPDATE; returns how many.</summary>
    Task<int> ResolveOpenReportsAsync(ReportTargetType type, Guid targetId, ReportStatus status, ReportAction action,
        string adminId, DateTime resolvedAt, CancellationToken cancellationToken = default);
}
