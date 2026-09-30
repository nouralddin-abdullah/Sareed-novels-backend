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

    public static ProfileCommentDTO ToDto(this ProfileComment comment, bool isLikedByViewer) => new()
    {
        Id = comment.Id,
        Content = comment.Content,
        AttachedImageUrl = comment.AttachedImageUrl,
        LikesCount = comment.LikesCount,
        IsLikedByCurrentUser = isLikedByViewer,
        CreatedAt = Utc.Of(comment.CreatedAt),
        UpdatedAt = Utc.Of(comment.UpdatedAt),
        IsReply = comment.ParentCommentId != null,
        ParentCommentId = comment.ParentCommentId,
        Novel = comment.Novel.ToDto(),
        Chapter = new ProfileChapterDTO { Id = comment.Chapter.Id, Title = comment.Chapter.Title, Number = comment.Chapter.Number },
        ParagraphId = comment.ParagraphId
    };

    private static ProfileNovelDTO ToDto(this ProfileNovel novel) => new()
    {
        Id = novel.Id,
        Slug = novel.Slug,
        Title = novel.Title,
        CoverImageUrl = novel.CoverImageUrl
    };
}
