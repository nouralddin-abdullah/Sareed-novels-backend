namespace Application.Users.DTOS;

// The items of a member's review and comment lists (GET /api/User/{userName}/reviews and /comments, #54). Field names
// follow the novel's review list (ReviewsDTO) and the comment lists (CommentsDTO). Dates are UTC, sent with "Z".

/// <summary>An item of GET /api/User/{userName}/reviews: one of the member's reviews, with its novel.</summary>
public class ProfileReviewDTO
{
    public Guid Id { get; set; }
    public decimal WritingQualityScore { get; set; }
    public decimal UpdatingStabilityScore { get; set; }
    public decimal CharacterDevelopmentScore { get; set; }
    public decimal WorldBuildingScore { get; set; }
    /// <summary>The review's rating: the average of its four scores.</summary>
    public decimal TotalAverageScore { get; set; }
    public string? Content { get; set; }
    /// <summary>The text gives away the story: the apps hide it until the reader asks to see it.</summary>
    public bool IsSpoiler { get; set; }
    public int LikeCount { get; set; }
    /// <summary>Whether the signed-in viewer liked it; false when signed out.</summary>
    public bool IsLikedByCurrentUser { get; set; }
    public DateTime CreatedAt { get; set; }
    /// <summary>When its author last edited it (#34); null when never.</summary>
    public DateTime? UpdatedAt { get; set; }
    public ProfileNovelDTO Novel { get; set; } = default!;
}

/// <summary>
/// An item of GET /api/User/{userName}/comments: one of the member's comments on a chapter or a paragraph, or a reply
/// in such a thread, with where it was written.
/// </summary>
public class ProfileCommentDTO
{
    public Guid Id { get; set; }
    public string Content { get; set; } = default!;
    public string? AttachedImageUrl { get; set; }
    public int LikesCount { get; set; }
    /// <summary>Whether the signed-in viewer liked it; false when signed out.</summary>
    public bool IsLikedByCurrentUser { get; set; }
    public DateTime CreatedAt { get; set; }
    /// <summary>When it was last edited; null when never (comments can't be edited yet).</summary>
    public DateTime? UpdatedAt { get; set; }
    /// <summary>Whether it answers another comment: <see cref="ParentCommentId"/>, in whose thread it is.</summary>
    public bool IsReply { get; set; }
    public Guid? ParentCommentId { get; set; }
    public ProfileNovelDTO Novel { get; set; } = default!;
    /// <summary>The chapter it was written on; for a paragraph comment, the paragraph's chapter.</summary>
    public ProfileChapterDTO Chapter { get; set; } = default!;
    /// <summary>The paragraph it was written on; null for a comment on the chapter itself.</summary>
    public Guid? ParagraphId { get; set; }
}

/// <summary>The novel a listed review or comment is on.</summary>
public class ProfileNovelDTO
{
    public Guid Id { get; set; }
    public string Slug { get; set; } = default!;
    public string Title { get; set; } = default!;
    public string CoverImageUrl { get; set; } = default!;
}

/// <summary>The chapter a listed comment is on.</summary>
public class ProfileChapterDTO
{
    public Guid Id { get; set; }
    public string Title { get; set; } = default!;
    /// <summary>
    /// The chapter's number as readers see it: its position among the novel's published chapters, from 1, as in the
    /// novel's chapter list and the library's lastReadChapterNumber. It changes when chapters before it are published,
    /// unpublished, deleted or reordered.
    /// </summary>
    public int Number { get; set; }
}
