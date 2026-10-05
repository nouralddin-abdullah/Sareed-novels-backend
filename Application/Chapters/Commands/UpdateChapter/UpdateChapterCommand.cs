using MediatR;

namespace Application.Chapters.Commands.UpdateChapter;

public class UpdateChapterCommand(Guid chapterId, Guid novelId, string? title, string? status, string? content) : IRequest<UpdateChapterResult>
{
    public Guid ChapterId { get; set; } = chapterId;
    public Guid NovelId { get; set; } = novelId;
    public string? Title { get; set; } = title;
    public string? Status { get; set; } = status;
    public string? Content { get; set; } = content;

    /// <summary>
    /// The chapter's revision the editor's copy was loaded at (#75). Given with a title or text and not the chapter's
    /// revision: 409 ChapterChanged, nothing saved. Left out: no check, as before. Not checked for a save of the status
    /// or the schedule alone (#77), which doesn't touch the text.
    /// </summary>
    public int? BaseRevision { get; set; }

    /// <summary>
    /// Matches and checks everything a save would (#75: ?dryRun=true), the revision included, saves nothing, and
    /// answers what the save would delete (<see cref="UpdateChapterResult.Preview"/>).
    /// </summary>
    public bool DryRun { get; set; }

    /// <summary>Whether this save sets or cancels the draft's schedule (#77); false keeps it as it is.</summary>
    public bool SetsSchedule { get; set; }

    /// <summary>With <see cref="SetsSchedule"/>: when the draft publishes itself, or null to cancel its schedule.</summary>
    public DateTime? PublishAt { get; set; }
}
