using Application.Users;
using Domain.Entities;
using Domain.Exceptions;
using Domain.Moderation;
using Domain.Repositories;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Application.Reports.Commands.CreateReport;

public class CreateReportCommandHandler(
    ILogger<CreateReportCommandHandler> logger,
    IUserContext userContext,
    IReportsRepository reportsRepository,
    TimeProvider time) : IRequestHandler<CreateReportCommand, CreateReportResult>
{
    /// <summary>
    /// Reports one account can make per <see cref="PerUserWindow"/> (duplicates don't count). The endpoint also has a
    /// per-address limit (RateLimitPolicies.Reports).
    /// </summary>
    public const int PerUserLimit = 20;

    public static readonly TimeSpan PerUserWindow = TimeSpan.FromHours(1);

    public async Task<CreateReportResult> Handle(CreateReportCommand request, CancellationToken cancellationToken)
    {
        var currentUser = userContext.GetCurrentUser() ?? throw new ForbidException("سجّل الدخول للمتابعة", "NotSignedIn");

        // CreateReportCommandValidator has checked all three.
        EnumNames.TryParse<ReportTargetType>(request.TargetType, out var targetType);
        EnumNames.TryParse<ReportReason>(request.Reason, out var reason);
        var targetId = Guid.Parse(request.TargetId!);

        var target = await reportsRepository.FindTargetAsync(targetType, targetId, currentUser.Id, cancellationToken)
            ?? throw new NotFoundException("المحتوى الذي تحاول الإبلاغ عنه غير موجود", "TargetNotFound");

        if (string.Equals(target.OwnerId, currentUser.Id, StringComparison.OrdinalIgnoreCase))
        {
            throw new BadRequestException("لا يمكنك الإبلاغ عن نفسك أو عن محتواك", "CannotReportOwnContent");
        }

        // Reporting the same thing again while the first report is open changes nothing.
        var open = await reportsRepository.GetOpenReportAsync(currentUser.Id, targetType, targetId, cancellationToken);
        if (open != null)
        {
            return new CreateReportResult(ReportDto.From(open), Created: false);
        }

        var now = time.GetUtcNow().UtcDateTime;
        if (await reportsRepository.CountCreatedSinceAsync(currentUser.Id, now - PerUserWindow, cancellationToken) >= PerUserLimit)
        {
            logger.LogWarning("User {UserId} reached the report limit", currentUser.Id);
            throw new TooManyRequestsException("أرسلت بلاغات كثيرة خلال وقت قصير، حاول مرة أخرى لاحقاً", "TooManyReports");
        }

        var (report, created) = await reportsRepository.AddAsync(new Report
        {
            Id = Guid.NewGuid(),
            ReporterId = currentUser.Id,
            TargetType = targetType,
            TargetId = targetId,
            TargetOwnerId = target.OwnerId,
            TargetExcerpt = target.Excerpt,
            Reason = reason,
            Details = string.IsNullOrWhiteSpace(request.Details) ? null : request.Details.Trim(),
            Status = ReportStatus.Open,
            CreatedAt = now
        }, cancellationToken);

        if (created)
        {
            logger.LogInformation("User {UserId} reported {TargetType} {TargetId} ({Reason}): report {ReportId}",
                currentUser.Id, targetType, targetId, reason, report.Id);
        }
        return new CreateReportResult(ReportDto.From(report), created);
    }
}
