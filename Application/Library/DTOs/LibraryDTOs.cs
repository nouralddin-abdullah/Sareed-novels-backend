namespace Application.Library.DTOs;

public class ReadingProgressDTO
{
    public Guid NovelId { get; set; }
    public string Title { get; set; } = default!;
    public string Slug { get; set; } = default!;
    public string CoverImageUrl { get; set; } = default!;
    public int TotalChapters { get; set; }
    public decimal TotalAverageScore { get; set; }
    public int TotalViews { get; set; }
    public Guid LastReadChapterId { get; set; }
    public int LastReadChapterNumber { get; set; }
    public string LastReadChapterTitle { get; set; } = default!;
    public decimal ProgressPercentage { get; set; }
    public DateTime LastReadAt { get; set; }

    /// <summary>Whether the novel's new chapters notify the reader (#33); PATCH /api/library/novel/{novelId} sets it.</summary>
    public bool NotifyNewChapters { get; set; }

    /// <summary>
    /// When the newest published chapter came out, UTC ("Z"); null when the novel has no published chapter (#33). The
    /// app shows «فصول جديدة» when it is later than <see cref="LastReadAt"/>.
    /// </summary>
    public DateTime? LastChapterPublishedAt { get; set; }

    public NovelAuthorDTO Author { get; set; } = default!;
}

public class NovelAuthorDTO
{
    public string UserName { get; set; } = default!;
    public string DisplayName { get; set; } = default!;
    public string? ProfilePhoto { get; set; }
}

public class NovelProgressDTO
{
    public Guid NovelId { get; set; }
    public Guid? LastReadChapterId { get; set; }
    public int? LastReadChapterNumber { get; set; }
    public decimal ProgressPercentage { get; set; }
    public DateTime? LastReadAt { get; set; }

    /// <summary>Whether the novel's new chapters notify the reader (#33).</summary>
    public bool NotifyNewChapters { get; set; }
}
