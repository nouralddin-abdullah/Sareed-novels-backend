using Domain.Constants;

namespace Domain.Entities;

public class Chapter
{
    public Guid Id { get; set; } = default!;
    public Guid NovelId { get; set; } = default!;
    public Novel Novel { get; set; } = default!;
    public string Title { get; set; } = default!;
    public string Slug { get; set; } = default!;
    public string? Content { get; set; } // Made nullable for migration
    public string Status { get; set; } = "Draft";
    public int ChapterIndex { get; set; }
    public int? PublishedChapterSequence { get; set; } // NEW: For efficient querying
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// The version of the chapter's title and text (#75): 1 when created, one more with every save that changes the
    /// title or the text, never with a change of status alone. An editor sends the revision its copy was loaded at
    /// (baseRevision), and a save from an older copy is refused instead of overwriting newer text. Written only with
    /// the chapter's text held (ChapterParagraphsRepository.BeginEditAsync), from the revision read there.
    /// </summary>
    public int Revision { get; set; } = 1;

    /// <summary>When the chapter was last saved (UTC): created, or edited in any way, its status included (#75).</summary>
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    
    /// <summary>
    /// When the chapter first came out to readers (UTC): stamped the first time it is published and kept from then on,
    /// also when it is unpublished and published again, so it never comes out as new twice. Null while it has never
    /// been published. A draft published later is stamped when it is published, not when it was written (#33).
    /// Set it through <see cref="SetStatus"/>; saving an edit stores it only while the stored chapter has none, so the
    /// save that stores it is the chapter's one first publish (#39).
    /// </summary>
    public DateTime? PublishedAt { get; set; }

    /// <summary>
    /// When the draft publishes itself (UTC, #77), or null when it isn't scheduled. Only a draft has one: publishing the
    /// chapter, by hand or on schedule, clears it (<see cref="SetStatus"/>), and an edit that doesn't send one keeps it.
    /// Set with the chapter held as for a save of its text (ChapterParagraphsRepository.BeginEditAsync), so a save and
    /// the scheduled publish run one after the other.
    /// </summary>
    public DateTime? PublishAt { get; set; }

    /// <summary>
    /// How many words the chapter's text has (#77), by <c>ChapterWords</c>'s rule (whitespace-separated runs of what
    /// readers see, image captions included, with at least one letter or digit). Set when the chapter is created and
    /// with every save of its text, in the same transaction as its paragraphs; null for a chapter from before word
    /// counts until the startup backfill counts it.
    /// </summary>
    public int? WordsCount { get; set; }

    // NEW: Paragraphs
    public ICollection<ChapterParagraph> Paragraphs { get; set; } = new List<ChapterParagraph>();
    
    // Updated comment counts
    public int CommentsCount { get; set; } = 0; // Chapter-level only
    public int TotalCommentsCount { get; set; } = 0; // Chapter + paragraphs
    public int ParagraphsCount { get; set; } = 0;
    public int ViewsCount { get; set; } = 0;

    /// <summary>
    /// Sets the chapter's <see cref="Status"/>. Publishing it stamps <see cref="PublishedAt"/> with
    /// <paramref name="now"/> if it has never been published; unpublishing and publishing again keep the first stamp.
    /// Publishing also clears its schedule (<see cref="PublishAt"/>, #77): only a draft has one.
    /// </summary>
    public void SetStatus(string status, DateTime now)
    {
        Status = status;
        if (status == ChapterStatuses.Published)
        {
            PublishedAt ??= now;
            PublishAt = null;
        }
    }

    public void IncrementViewsCount()
    {
        ViewsCount++;
    }
}
