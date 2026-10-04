using Application.Comments;
using Application.Comments.DTOS;
using Application.Reports;
using Application.Users.DTOS;
using Domain.Profiles;

namespace Application.Users.Queries;

/// <summary>A member's listed reviews and comments (#54) as the API answers them.</summary>
internal static class ProfileListMapping
{
    public static ProfileReviewDTO ToDto(this ProfileReview review, bool isLikedByViewer) => new()
    {
        Id = review.Id,
        WritingQualityScore = review.WritingQualityScore,
        UpdatingStabilityScore = review.UpdatingStabilityScore,
        CharacterDevelopmentScore = review.CharacterDevelopmentScore,
        WorldBuildingScore = review.WorldBuildingScore,
        TotalAverageScore = review.TotalAverageScore,
        Content = review.Content,
        IsSpoiler = review.IsSpoiler,
        LikeCount = review.LikeCount,
        IsLikedByCurrentUser = isLikedByViewer,
        CreatedAt = Utc.Of(review.CreatedAt),
        UpdatedAt = Utc.Of(review.UpdatedAt),
        Novel = review.Novel.ToDto()
    };

    /// <summary>
    /// The comment as listed. <paramref name="likedByViewer"/> holds the comments the viewer liked among the page's
    /// comments and the ones they answer (empty when signed out). Its paragraph is quoted only when
    /// <paramref name="viewerReadsChapter"/>: the reader would show its chapter's text to the viewer
    /// (Chapters.ChapterAccess).
    /// </summary>
    public static ProfileCommentDTO ToDto(this ProfileComment comment, IReadOnlySet<Guid> likedByViewer,
        bool viewerReadsChapter) => new()
    {
        Id = comment.Id,
        Content = comment.Content,
        AttachedImageUrl = comment.AttachedImageUrl,
        LikesCount = comment.LikesCount,
        IsLikedByCurrentUser = likedByViewer.Contains(comment.Id),
        CreatedAt = Utc.Of(comment.CreatedAt),
        UpdatedAt = Utc.Of(comment.UpdatedAt),
        IsReply = comment.ParentCommentId != null,
        ParentCommentId = comment.ParentCommentId,
        ParentComment = comment.ParentComment?.ToDto(likedByViewer),
        Novel = comment.Novel.ToDto(),
        Chapter = new ProfileChapterDTO { Id = comment.Chapter.Id, Title = comment.Chapter.Title, Number = comment.Chapter.Number },
        ParagraphId = comment.Paragraph?.Id,
        ParagraphExcerpt = comment.Paragraph is { } paragraph && viewerReadsChapter ? ParagraphExcerpt.Of(paragraph.Content) : null
    };

    private static ProfileParentCommentDTO ToDto(this ProfileParentComment parent,
        IReadOnlySet<Guid> likedByViewer) => new()
    {
        Id = parent.Id,
        Content = parent.Content,
        AttachedImageUrl = parent.AttachedImageUrl,
        LikesCount = parent.LikesCount,
        IsLikedByCurrentUser = likedByViewer.Contains(parent.Id),
        CreatedAt = Utc.Of(parent.CreatedAt),
        TotalRepliesCount = parent.RepliesCount,
        User = new CommentUserDTO
        {
            Id = parent.User.Id,
            UserName = parent.User.UserName,
            DisplayName = parent.User.DisplayName,
            ProfilePhoto = parent.User.ProfilePhoto
        }
    };

    private static ProfileNovelDTO ToDto(this ProfileNovel novel) => new()
    {
        Id = novel.Id,
        Slug = novel.Slug,
        Title = novel.Title,
        CoverImageUrl = novel.CoverImageUrl
    };
}
