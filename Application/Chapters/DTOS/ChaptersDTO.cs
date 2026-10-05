using Application.Chapters.Paragraphs;
using Application.Novels.DTOS;
using Domain.Constants;
using Domain.Entities;

namespace Application.Chapters.DTOS;

public class ChaptersDTO
{
    public Guid Id { get; set; }
    public Guid NovelId { get; set; }
    public string Title { get; set; } = default!;
    public string Status { get; set; } = default!;
    public int ParagraphsCount { get; set; }
    public int TotalCommentsCount { get; set; }
    public int ViewsCount { get; set; }
    public DateTime CreatedAt { get; set; } = default!;
    /// <summary>
    /// When the chapter came out (#39), UTC and sent with "Z": the first time it was published, kept if it is
    /// unpublished and published again. Null while it has never been published (a draft). <see cref="CreatedAt"/> is
    /// when it was written, which for a draft published later is earlier.
    /// </summary>
    public DateTime? PublishedAt { get; set; }
    
    // Privilege System
    public bool IsLocked { get; set; } = false; // Is this chapter locked by privilege?
}

/// <summary>
/// A chapter in its author's list (<c>GET /api/myworks/{workId}/chapters</c>): the novel's list item, and what only the
/// author sees: what an editor needs (#75) and the writer extras (#77).
/// </summary>
public class ChaptersAuthorDTO : ChaptersDTO
{
    /// <summary>The chapter's revision (#75): sent back as baseRevision when the editor saves this chapter.</summary>
    public int Revision { get; set; }

    /// <summary>When the chapter was last saved, UTC with "Z": created, or edited in any way, its status included.</summary>
    public DateTime UpdatedAt { get; set; }

    /// <summary>
    /// The chapter's words (#77, <c>ChapterWords</c>); null for a chapter from before word counts that the startup
    /// backfill hasn't counted yet.
    /// </summary>
    public int? WordsCount { get; set; }

    /// <summary>When the draft publishes itself (#77, UTC with "Z"); null when it isn't scheduled (always, once published).</summary>
    public DateTime? PublishAt { get; set; }
}

public class ChapterSingleAuthorDTO
{
    public Guid Id { get; set; }
    public Guid NovelId { get; set; }
    public string Title { get; set; } = default!;
    public string Slug { get; set; } = default!;
    public string Status { get; set; } = default!;
    public int ChapterIndex { get; set; }
    public DateTime CreatedAt { get; set; }
    /// <summary>
    /// When the chapter came out (#39), UTC and sent with "Z": the first time it was published, kept if it is
    /// unpublished and published again. Null while it has never been published (a draft). <see cref="CreatedAt"/> is
    /// when it was written, which for a draft published later is earlier.
    /// </summary>
    public DateTime? PublishedAt { get; set; }

    /// <summary>
    /// The chapter's revision (#75): 1 when created, one more with each save that changes its title or text. The editor
    /// sends it back as baseRevision, and a save from an older copy is refused (409 ChapterChanged).
    /// </summary>
    public int Revision { get; set; }

    /// <summary>When the chapter was last saved, UTC with "Z": created, or edited in any way, its status included.</summary>
    public DateTime UpdatedAt { get; set; }

    /// <summary>When the draft publishes itself (#77, UTC with "Z"); null when it isn't scheduled.</summary>
    public DateTime? PublishAt { get; set; }

    /// <summary>The chapter's words (#77, <see cref="ChaptersAuthorDTO.WordsCount"/>).</summary>
    public int? WordsCount { get; set; }

    public List<ChapterParagraphDTO> Paragraphs { get; set; } = new();
}

public class ChapterSingleReaderDTO
{
    public Guid Id { get; set; }
    public Guid NovelId { get; set; }
    public string Title { get; set; } = default!;
    public AuthorDTO Author { get; set; } = default!;
    public int CommentsCount { get; set; }
    public int TotalCommentsCount { get; set; }

    /// <summary>
    /// When the chapter came out (#39), UTC and sent with "Z": the first time it was published, kept if it is
    /// unpublished and published again. Null while it has never been published (a draft, which only its author can
    /// open).
    /// </summary>
    public DateTime? PublishedAt { get; set; }
    public string? NextChapterSlug { get; set; }
    public List<ChapterParagraphDTO> Paragraphs { get; set; } = new();
    
    // Privilege System
    public bool IsLocked { get; set; } = false; // Is this chapter locked?
    public string? LockMessage { get; set; } // Message to display when locked
}

/// <summary>
/// A chapter paragraph in chapter format v1 (#74), as the readers, the author's editor and the created chapter get it,
/// whatever is stored (<see cref="ChapterFormat.Read"/>).
/// </summary>
public class ChapterParagraphDTO
{
    public Guid Id { get; set; }

    /// <summary>
    /// For text, center and quote: inline HTML (text, strong, em, u, s, br; text HTML-escaped). For a break:
    /// <c>* * *</c>. For an image: the picture's http(s) address.
    /// </summary>
    public string Content { get; set; } = default!;
    public int OrderIndex { get; set; }

    /// <summary>The paragraph's kind: text, center, quote, break or image (Domain.Constants.ParagraphKinds).</summary>
    public string ContentType { get; set; } = ParagraphKinds.Text;

    /// <summary>An image's caption, plain text (not HTML); null for every other kind and for no caption.</summary>
    public string? Caption { get; set; }
    public int CommentsCount { get; set; }

    public static ChapterParagraphDTO Of(ChapterParagraph paragraph)
    {
        var formatted = ParagraphRows.Read(paragraph);
        return new ChapterParagraphDTO
        {
            Id = paragraph.Id,
            Content = formatted.Content,
            OrderIndex = paragraph.OrderIndex,
            ContentType = formatted.Kind,
            Caption = formatted.Caption,
            CommentsCount = paragraph.CommentsCount
        };
    }
}

