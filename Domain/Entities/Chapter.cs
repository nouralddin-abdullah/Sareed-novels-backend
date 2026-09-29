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
    /// When the chapter first came out to readers (UTC): stamped the first time it is published and kept from then on,
    /// also when it is unpublished and published again, so it never comes out as new twice. Null while it has never
    /// been published. A draft published later is stamped when it is published, not when it was written (#33).
    /// Set it through <see cref="SetStatus"/>.
    /// </summary>
    public DateTime? PublishedAt { get; set; }

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
    /// </summary>
    public void SetStatus(string status, DateTime now)
    {
        Status = status;
        if (status == ChapterStatuses.Published)
        {
            PublishedAt ??= now;
        }
    }

    public void IncrementViewsCount()
    {
        ViewsCount++;
    }
}
