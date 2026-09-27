using Domain.Moderation;
using FluentValidation;
using MediatR;

namespace Application.Reports.Commands.ResolveReport;

/// <summary>The body of PATCH /api/admin/reports/{id}.</summary>
public class ResolveReportRequest
{
    /// <summary>Dismiss | RemoveContent | SuspendUser (nullable, so a missing one gets the validator's Arabic message).</summary>
    public string? Action { get; set; }
    /// <summary>SuspendUser: how many days (1..3650); left out for a permanent suspension.</summary>
    public int? SuspensionDays { get; set; }
}

public class ResolveReportRequestValidator : AbstractValidator<ResolveReportRequest>
{
    public ResolveReportRequestValidator()
    {
        RuleFor(r => r.Action)
            .Must(action => EnumNames.TryParse<ReportAction>(action, out _))
            .WithMessage("الإجراء غير صالح: Dismiss أو RemoveContent أو SuspendUser");

        RuleFor(r => r.SuspensionDays)
            .InclusiveBetween(1, Suspension.MaxDays)
            .When(r => r.SuspensionDays != null)
            .WithMessage($"مدة الإيقاف بين 1 و{Suspension.MaxDays} يوماً، أو بلا مدة للإيقاف الدائم");
    }
}

/// <summary>
/// An admin acts on a report, and with it on every open report on the same target: Dismiss (nothing wrong),
/// RemoveContent (delete the item the way its author would, counters included), or SuspendUser (its author, or the
/// reported user, for <see cref="SuspensionDays"/> days or for good).
/// </summary>
public class ResolveReportCommand(Guid reportId, string action, int? suspensionDays) : IRequest<ResolveReportResult>
{
    public Guid ReportId { get; } = reportId;
    public string Action { get; } = action;
    public int? SuspensionDays { get; } = suspensionDays;
}
