using Domain.Constants;

namespace Application.Chapters.Scheduling;

/// <summary>
/// Scheduling a draft chapter to publish itself (#77): <c>publishAt</c> on creating a chapter or saving one. Only a draft
/// is scheduled, and only for a time to come. When the time comes, the chapter is published by
/// <see cref="ScheduledChapterPublisher"/>, as its author would publish it.
/// </summary>
public static class ChapterSchedule
{
    public const string NotDraftCode = "ScheduleRequiresDraft";
    public const string NotDraftMessage = "يمكن تحديد موعد نشر للمسودات فقط";

    /// <summary>
    /// <see cref="NotDraftCode"/> for cancelling the schedule of a chapter that has come out (#88), e.g. published by
    /// the schedule just before: there is no schedule left to cancel, and the app must not show it as a draft.
    /// </summary>
    public const string AlreadyPublishedMessage = "نُشر هذا الفصل بالفعل";

    public const string InPastCode = "PublishAtInPast";
    public const string InPastMessage = "موعد النشر يجب أن يكون في المستقبل";

    /// <summary>
    /// A time a client sent, as UTC: one with an offset (or "Z") is converted, one without is taken as UTC, as the API's
    /// times are.
    /// </summary>
    public static DateTime? ToUtc(DateTime? value) => value switch
    {
        null => null,
        { Kind: DateTimeKind.Local } local => local.ToUniversalTime(),
        { } other => DateTime.SpecifyKind(other, DateTimeKind.Utc)
    };

    /// <summary>
    /// Why a chapter whose status will be <paramref name="status"/> can't be scheduled for <paramref name="publishAt"/>,
    /// <paramref name="now"/>; null when it can, or when <paramref name="publishAt"/> is null (no schedule). For a new
    /// chapter; a save of one sets or cancels its schedule by <see cref="SaveRefusal"/>.
    /// </summary>
    public static ScheduleRefusal? Refusal(DateTime? publishAt, string? status, DateTime now) =>
        ToUtc(publishAt) is not { } at ? null
        : status != ChapterStatuses.Draft ? new ScheduleRefusal(NotDraftCode, NotDraftMessage)
        : at <= now ? new ScheduleRefusal(InPastCode, InPastMessage)
        : null;

    /// <summary>
    /// Why a save that sends <c>publishAt</c> (<paramref name="publishAt"/>: a time, or null to cancel the schedule)
    /// and <paramref name="sentStatus"/> (null keeps the status) can't, on a chapter that is <paramref name="status"/>
    /// as the save reads it; null when it can. A time is checked as for a new chapter (<see cref="Refusal"/>), against
    /// the status the save leaves. Cancelling is for a draft, scheduled or not, and for a save that makes the chapter a
    /// draft again; a save that publishes a draft may cancel its schedule too. A chapter that has come out and stays
    /// out has no schedule to cancel (#88): <see cref="NotDraftCode"/>, with <see cref="AlreadyPublishedMessage"/>.
    /// </summary>
    public static ScheduleRefusal? SaveRefusal(DateTime? publishAt, string status, string? sentStatus, DateTime now)
    {
        if (publishAt is not null)
        {
            return Refusal(publishAt, sentStatus ?? status, now);
        }

        var staysOut = status != ChapterStatuses.Draft && (sentStatus ?? status) != ChapterStatuses.Draft;
        return staysOut ? new ScheduleRefusal(NotDraftCode, AlreadyPublishedMessage) : null;
    }
}

/// <summary>A refused <c>publishAt</c>: the code clients branch on and the Arabic message (<see cref="ChapterSchedule"/>).</summary>
public sealed record ScheduleRefusal(string Code, string Message);
