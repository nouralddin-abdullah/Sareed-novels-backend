using Application.Services;
using Application.Users;
using Domain.Exceptions;
using Domain.Moderation;
using Domain.Repositories;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Application.Reports.Commands.ResolveReport;

public class ResolveReportCommandHandler(
    ILogger<ResolveReportCommandHandler> logger,
    IUserContext userContext,
    IReportsRepository reportsRepository,
    ICommentsRepository commentsRepository,
    IReviewsRepository reviewsRepository,
    IPostsRepository postsRepository,
    INovelsRepository novelsRepository,
    IReadingListsRepository readingListsRepository,
    IAccountSuspensionService suspensions,
    TimeProvider time) : IRequestHandler<ResolveReportCommand, ResolveReportResult>
{
    public async Task<ResolveReportResult> Handle(ResolveReportCommand request, CancellationToken cancellationToken)
    {
        var admin = userContext.GetCurrentUser() ?? throw new ForbidException("سجّل الدخول للمتابعة", "NotSignedIn");
        var report = await reportsRepository.GetByIdAsync(request.ReportId, cancellationToken)
            ?? throw new NotFoundException("البلاغ غير موجود", "ReportNotFound");

        // ResolveReportRequestValidator has checked it.
        EnumNames.TryParse<ReportAction>(request.Action, out var action);
        var result = new ResolveReportResult { ReportId = report.Id, Action = action.ToString() };
        var now = time.GetUtcNow().UtcDateTime;

        switch (action)
        {
            case ReportAction.RemoveContent:
                if (report.TargetType == ReportTargetType.User)
                {
                    throw new BadRequestException("لا يمكن حذف حساب مستخدم من البلاغات، استخدم إيقاف المستخدم (SuspendUser)", "InvalidAction");
                }
                // Idempotent: content that is gone already just gets its reports closed.
                result.ContentRemoved = await RemoveContentAsync(report.TargetType, report.TargetId);
                break;

            case ReportAction.SuspendUser:
                var userId = report.TargetOwnerId;
                if (string.Equals(userId, admin.Id, StringComparison.OrdinalIgnoreCase))
                {
                    throw new BadRequestException("لا يمكنك إيقاف حسابك", "CannotSuspendSelf");
                }
                var until = await suspensions.SuspendAsync(userId, Suspension.Until(request.SuspensionDays, now), cancellationToken)
                    ?? throw new NotFoundException("المستخدم غير موجود", "UserNotFound");
                result.SuspendedUserId = userId;
                result.SuspendedPermanently = Suspension.IsPermanent(until);
                result.SuspendedUntil = result.SuspendedPermanently ? null : Utc.Of(until);
                break;
        }

        result.ResolvedReports = await reportsRepository.ResolveOpenReportsAsync(
            report.TargetType, report.TargetId,
            action == ReportAction.Dismiss ? ReportStatus.Dismissed : ReportStatus.Resolved,
            action, admin.Id, now, cancellationToken);

        logger.LogInformation(
            "Admin {AdminId} took {Action} on report {ReportId} ({TargetType} {TargetId}): {Resolved} open reports closed, content removed: {Removed}, suspended: {SuspendedUserId}",
            admin.Id, action, report.Id, report.TargetType, report.TargetId, result.ResolvedReports, result.ContentRemoved, result.SuspendedUserId);
        return result;
    }

    /// <summary>
    /// Deletes the item the same way its author (or the app) does, so counters, replies, likes and notifications stay
    /// consistent. False when it was already gone.
    /// </summary>
    private async Task<bool> RemoveContentAsync(ReportTargetType type, Guid id) => type switch
    {
        // Hard delete with the reply tree, likes, notifications and counters (#15's path).
        ReportTargetType.Comment => await commentsRepository.RemoveCommentAsync(id),
        // With its likes; the reviewer's count and the novel's review stats are recomputed.
        ReportTargetType.Review => await reviewsRepository.GetReviewById(id) is { } review && await reviewsRepository.DeleteReview(review),
        ReportTargetType.Post => await postsRepository.DeletePost(id),
        // Soft delete, out of the rankings, as the author's delete.
        ReportTargetType.Novel => await novelsRepository.SoftDeleteAsync(id),
        ReportTargetType.ReadingList => await readingListsRepository.DeleteAsync(id),
        _ => false
    };
}
