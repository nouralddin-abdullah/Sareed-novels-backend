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
    /// <paramref name="now"/>; null when it can, or when <paramref name="publishAt"/> is null (no schedule, or cancelling
    /// one, which is always allowed).
    /// </summary>
    public static ScheduleRefusal? Refusal(DateTime? publishAt, string? status, DateTime now) =>
        ToUtc(publishAt) is not { } at ? null
        : status != ChapterStatuses.Draft ? new ScheduleRefusal(NotDraftCode, NotDraftMessage)
        : at <= now ? new ScheduleRefusal(InPastCode, InPastMessage)
        : null;
}

/// <summary>A refused <c>publishAt</c>: the code clients branch on and the Arabic message (<see cref="ChapterSchedule"/>).</summary>
public sealed record ScheduleRefusal(string Code, string Message);
