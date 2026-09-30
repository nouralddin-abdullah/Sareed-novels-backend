using Application.Novels.DTOS;

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

public class ChapterParagraphDTO
{
    public Guid Id { get; set; }
    public string Content { get; set; } = default!;
    public int OrderIndex { get; set; }
    public string ContentType { get; set; } = "text";
    public int CommentsCount { get; set; }
}

