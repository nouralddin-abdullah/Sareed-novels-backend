using MediatR;

namespace Application.Reports.Commands.CreateReport;

/// <summary>
/// POST /api/reports. The enum fields take the names in Domain.Moderation (ignoring case): targetType
/// Comment|Review|Post|User|Novel|ReadingList, reason Spam|Harassment|Sexual|Violence|HateSpeech|Spoiler|Other.
/// </summary>
public class CreateReportCommand : IRequest<CreateReportResult>
{
    // Nullable, so a missing field gets the validator's Arabic message rather than MVC's English "required" one.
    public string? TargetType { get; set; }
    /// <summary>The reported item's id (a user's id for a user), a GUID.</summary>
    public string? TargetId { get; set; }
    public string? Reason { get; set; }
    /// <summary>Optional, at most 1000 characters.</summary>
    public string? Details { get; set; }
}

/// <param name="Created">False when the reporter already had an open report on the target: that one is returned and nothing is saved.</param>
public record CreateReportResult(ReportDto Report, bool Created);
