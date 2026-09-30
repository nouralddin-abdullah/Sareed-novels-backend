using Domain.Competitions;
using Domain.Entities;
using Domain.Exceptions;

namespace Application.Competitions;

/// <summary>
/// What the competition endpoints accept for a status and a schedule (#42): one set of rules for the list's
/// <c>?status=</c> filter and for an admin creating or updating a competition. The messages are Arabic.
/// </summary>
public static class CompetitionRules
{
    public const string InvalidStatusCode = "InvalidStatus";
    public const string InvalidStatusMessage = "حالة المسابقة غير صالحة: Upcoming أو Participation أو Judging أو Completed";

    public const string InvalidScheduleCode = "InvalidSchedule";
    public const string InvalidScheduleMessage = "يجب أن يكون موعد انتهاء المشاركة بعد موعد بدئها";

    /// <summary>
    /// One of the four statuses, in any letter case, as written in <see cref="CompetitionStatus"/>; anything else is
    /// 400 <see cref="InvalidStatusCode"/>. Only these four names are stored or filtered by, which the status rule
    /// relies on (<see cref="CompetitionSchedule"/>).
    /// </summary>
    public static string ParseStatus(string? value) =>
        CompetitionSchedule.TryParseStatus(value, out var status)
            ? status
            : throw new BadRequestException(InvalidStatusMessage, InvalidStatusCode);

    /// <summary>
    /// Participation must end after it starts; otherwise the dates would never open the competition. 400
    /// <see cref="InvalidScheduleCode"/> when it doesn't.
    /// </summary>
    public static void EnsureParticipationWindow(DateTime participationStartDate, DateTime participationEndDate)
    {
        if (participationEndDate <= participationStartDate)
        {
            throw new BadRequestException(InvalidScheduleMessage, InvalidScheduleCode);
        }
    }
}
