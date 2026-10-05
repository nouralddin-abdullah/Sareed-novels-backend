namespace Domain.Profiles;

// A member's reviews and comments as the lists on their profile show them (#54), read by IProfileListsRepository.

/// <summary>The novel a listed review or comment is on, and whose novel it is.</summary>
public sealed record ProfileNovel(Guid Id, string Slug, string Title, string CoverImageUrl, string AuthorId);

/// <summary>
/// The chapter a listed comment was written on. <see cref="Number"/> is the chapter's number as readers see it: its
/// position among the novel's published chapters in reading order, from 1 (the library's lastReadChapterNumber is
/// counted the same way).
/// </summary>
public sealed record ProfileChapter(Guid Id, string Title, int Number);

/// <summary>
/// The paragraph a listed comment was written on, with its stored text: content, kind and caption (chapter format v1,
/// #74, or the editor's HTML in rows from before it).
/// </summary>
public sealed record ProfileParagraph(Guid Id, string Content, string ContentType, string? Caption);

/// <summary>
/// A comment's author as the comment lists show them, with the names they have now. A deleted account shows as it is
/// kept: anonymized, «مستخدم محذوف» with a "deleted-..." user name and no photo (DeletedAccounts).
/// </summary>
public sealed record ProfileUser(string Id, string UserName, string DisplayName, string? ProfilePhoto);

/// <summary>
/// The comment a listed reply answers (#60): its thread's top-level comment as the comment lists show it (#67), with
/// its full text, its picture, its likes, when it was written, its author, and <see cref="RepliesCount"/>: the replies
/// under it that its thread shows the viewer it was read for (not those by members the viewer blocked).
/// </summary>
public sealed record ProfileParentComment(
    Guid Id,
    string Content,
    string? AttachedImageUrl,
    int LikesCount,
    DateTime CreatedAt,
    int RepliesCount,
    ProfileUser User);

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
/// written: the novel, the chapter (for a paragraph comment, the paragraph's), and the paragraph if any. A reply also
/// has the comment it answers (<see cref="ParentComment"/>), unless the viewer it was read for blocked that comment's
/// author: then only <see cref="ParentCommentId"/>.
/// </summary>
public sealed record ProfileComment(
    Guid Id,
    string Content,
    string? AttachedImageUrl,
    int LikesCount,
    DateTime CreatedAt,
    DateTime? UpdatedAt,
    Guid? ParentCommentId,
    ProfileParentComment? ParentComment,
    ProfileNovel Novel,
    ProfileChapter Chapter,
    ProfileParagraph? Paragraph);

/// <summary>How many reviews and comments a member's lists hold.</summary>
public sealed record ProfileCounts(int Reviews, int Comments);
