namespace Domain.Profiles;

// A member's reviews and comments as the lists on their profile show them (#54), read by IProfileListsRepository.

/// <summary>The novel a listed review or comment is on.</summary>
public sealed record ProfileNovel(Guid Id, string Slug, string Title, string CoverImageUrl);

/// <summary>
/// The chapter a listed comment was written on. <see cref="Number"/> is the chapter's number as readers see it: its
/// position among the novel's published chapters in reading order, from 1 (the library's lastReadChapterNumber is
/// counted the same way).
/// </summary>
public sealed record ProfileChapter(Guid Id, string Title, int Number);

/// <summary>One of a member's reviews, with the novel it is on.</summary>
public sealed record ProfileReview(
    Guid Id,
    decimal WritingQualityScore,
    decimal UpdatingStabilityScore,
    decimal CharacterDevelopmentScore,
    decimal WorldBuildingScore,
    decimal TotalAverageScore,
    string? Content,
    bool IsSpoiler,
    int LikeCount,
    DateTime CreatedAt,
    DateTime? UpdatedAt,
    ProfileNovel Novel);

/// <summary>
/// One of a member's comments on a chapter or on one of its paragraphs, or a reply in such a thread, with where it was
/// written: the novel, the chapter (for a paragraph comment, the paragraph's), and the paragraph if any.
/// </summary>
public sealed record ProfileComment(
    Guid Id,
    string Content,
    string? AttachedImageUrl,
    int LikesCount,
    DateTime CreatedAt,
    DateTime? UpdatedAt,
    Guid? ParentCommentId,
    ProfileNovel Novel,
    ProfileChapter Chapter,
    Guid? ParagraphId);

/// <summary>How many reviews and comments a member's lists hold.</summary>
public sealed record ProfileCounts(int Reviews, int Comments);
