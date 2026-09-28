using Application.Common;
using Domain.Entities;
using Domain.Exceptions;
using Domain.Moderation;
using Domain.Repositories;
using MediatR;

namespace Application.Reports.Queries.GetReports;

/// <summary>
/// GET /api/admin/reports: the moderators' list. <see cref="Status"/> is Open (the default), Resolved, Dismissed or
/// All; open reports come oldest first (the queue), others newest first.
/// </summary>
public class GetReportsQuery(string? status, int pageNumber, int pageSize) : IRequest<PagedResult<AdminReportDto>>
{
    public string? Status { get; } = status;
    public int PageNumber { get; } = pageNumber;
    public int PageSize { get; } = pageSize;
}

public class GetReportsQueryHandler(
    IReportsRepository reportsRepository,
    IUsersRepository usersRepository,
    TimeProvider time) : IRequestHandler<GetReportsQuery, PagedResult<AdminReportDto>>
{
    public async Task<PagedResult<AdminReportDto>> Handle(GetReportsQuery request, CancellationToken cancellationToken)
    {
        var status = ParseStatus(request.Status);
        var (pageNumber, pageSize) = Paging.Clamp(request.PageNumber, request.PageSize);
        var (reports, totalCount) = await reportsRepository.GetPageAsync(status, pageNumber, pageSize, cancellationToken);

        // What the page needs, in a fixed number of queries: its targets as they are now, their open reports, and the
        // people involved.
        var targets = reports.Select(r => (r.TargetType, r.TargetId)).Distinct().ToList();
        var states = await reportsRepository.DescribeTargetsAsync(targets, cancellationToken);
        var openCounts = await reportsRepository.CountOpenReportsAsync(targets, cancellationToken);
        string OwnerOf(Report r) => states[(r.TargetType, r.TargetId)].OwnerId ?? r.TargetOwnerId;
        var users = await usersRepository.GetByIdsAsync(
            reports.Select(r => r.ReporterId).Concat(reports.Select(OwnerOf)).ToList(), cancellationToken);

        var now = time.GetUtcNow().UtcDateTime;
        var items = reports.Select(report =>
        {
            var state = states[(report.TargetType, report.TargetId)];
            return new AdminReportDto
            {
                Id = report.Id,
                Reason = report.Reason.ToString(),
                Details = report.Details,
                Status = report.Status.ToString(),
                CreatedAt = Utc.Of(report.CreatedAt),
                Reporter = users.TryGetValue(report.ReporterId, out var reporter) ? UserCard(reporter, new ReportUserDto()) : null,
                Target = new AdminReportTargetDto
                {
                    Type = report.TargetType.ToString(),
                    Id = report.TargetId,
                    IsDeleted = !state.Exists,
                    Excerpt = report.TargetExcerpt,
                    CurrentExcerpt = state.Excerpt,
                    Link = state.Link,
                    Owner = users.TryGetValue(OwnerOf(report), out var owner) ? OwnerCard(owner, now) : null
                },
                OpenReportsOnTarget = openCounts.GetValueOrDefault((report.TargetType, report.TargetId)),
                Action = report.Resolution?.ToString(),
                ResolvedAt = Utc.Of(report.ResolvedAt),
                ResolvedById = report.ResolvedById
            };
        }).ToList();

        return new PagedResult<AdminReportDto>(items, totalCount, pageSize, pageNumber);
    }

    private static ReportStatus? ParseStatus(string? status)
    {
        if (string.IsNullOrWhiteSpace(status))
        {
            return ReportStatus.Open;
        }
        if (string.Equals(status.Trim(), "All", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }
        return EnumNames.TryParse<ReportStatus>(status, out var parsed)
            ? parsed
            : throw new BadRequestException("حالة البلاغ غير صالحة: Open أو Resolved أو Dismissed أو All", "InvalidStatus");
    }

    private static T UserCard<T>(User user, T card) where T : ReportUserDto
    {
        card.UserId = user.Id;
        card.UserName = user.UserName!;
        card.DisplayName = user.DisplayName;
        card.ProfilePhoto = user.ProfilePhoto;
        return card;
    }

    private static ReportOwnerDto OwnerCard(User user, DateTime now)
    {
        var card = UserCard(user, new ReportOwnerDto());
        card.IsSuspended = Suspension.IsActive(user.SuspendedUntil, now);
        card.SuspendedPermanently = card.IsSuspended && Suspension.IsPermanent(user.SuspendedUntil!.Value);
        card.SuspendedUntil = Utc.SuspensionEnd(user.SuspendedUntil, now);
        return card;
    }
}
