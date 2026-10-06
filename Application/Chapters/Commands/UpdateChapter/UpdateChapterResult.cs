using System.Text.Json.Serialization;
using Application.Users.Commands.FollowUser;

namespace Application.Chapters.Commands.UpdateChapter;

/// <summary>
/// A saved chapter: today's <c>success</c> and <c>message</c>, and the chapter as the save stored it: its revision
/// (#75), status and schedule (#88).
/// </summary>
public class UpdateChapterResult : OperationResult
{
    /// <summary>
    /// The chapter's revision now: one more than before when the save changed its title or text, the same when it
    /// didn't (a change of status alone, or the title and text sent back unchanged).
    /// </summary>
    public int Revision { get; set; }

    /// <summary>
    /// The chapter's status as the save stored it (#88), <c>Draft</c> or <c>Published</c>: the one sent, or the one it
    /// had when the save read it, which the schedule may have published just before.
    /// </summary>
    public string? Status { get; set; }

    /// <summary>
    /// When the draft publishes itself as the save stored it (#77, #88), UTC with "Z": the time sent, the one it had,
    /// or null when it isn't scheduled (cancelled, never scheduled, or published, which clears it).
    /// </summary>
    public DateTime? PublishAt { get; set; }

    /// <summary>A dry run's answer (?dryRun=true), sent instead of this result; null otherwise.</summary>
    [JsonIgnore]
    public ChapterSavePreview? Preview { get; set; }
}

/// <summary>
/// What a save would delete (#75, PATCH .../chapter/{chapterId}?dryRun=true): the paragraphs it would remove, and the
/// comments that would go with them, so the app can warn before a save that deletes readers' comments.
/// </summary>
public class ChapterSavePreview
{
    public int ParagraphsRemoved { get; set; }

    /// <summary>
    /// The comments on those paragraphs, replies included, that readers see: comments their authors deleted are left
    /// out, as the comment counters leave them out (#66).
    /// </summary>
    public int CommentsDeleted { get; set; }

    /// <summary>Every paragraph the save would remove, in the chapter's order, with its comments as counted above.</summary>
    public List<RemovedParagraphPreview> Removed { get; set; } = [];
}

public class RemovedParagraphPreview
{
    public Guid ParagraphId { get; set; }
    public int CommentsCount { get; set; }
}
