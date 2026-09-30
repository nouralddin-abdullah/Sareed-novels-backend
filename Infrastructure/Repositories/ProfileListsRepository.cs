using Domain.Constants;
using Domain.Entities;
using Domain.Profiles;
using Domain.Repositories;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Repositories;

/// <inheritdoc />
/// <remarks>
/// <see cref="Reviews"/> and <see cref="Comments"/> are the one definition of each list: a page and its total are read
/// from them, and so are the profile's counts. Both start from the member's own rows: their reviews in the
/// (ReviewerId, NovelId) index, their comments in IX_Comments_UserId_CreatedAt, which holds every column the comment
/// list filters on, in its order. The places they were written are found by primary key, and whether a novel has a
/// published chapter on the chapters' (NovelId, Status) index.
/// </remarks>
internal sealed class ProfileListsRepository(ApplicationDbContext db) : IProfileListsRepository
{
    public async Task<(IReadOnlyList<ProfileReview> Items, int TotalCount)> GetReviewsAsync(string userId, int pageNumber,
        int pageSize, CancellationToken cancellationToken = default)
    {
        var listed = Reviews(userId);
        var totalCount = await listed.CountAsync(cancellationToken);
        var items = await listed
            .OrderByDescending(x => x.Review.CreatedAt)
            .ThenBy(x => x.Review.Id)
            .Skip((pageNumber - 1) * pageSize)
            .Take(pageSize)
            .Select(x => new ProfileReview(
                x.Review.Id,
                x.Review.WritingQualityScore,
                x.Review.UpdatingStabilityScore,
                x.Review.CharacterDevelopmentScore,
                x.Review.WorldBuildingScore,
                x.Review.TotalAverageScore,
                x.Review.Content,
                x.Review.IsSpoiler,
                x.Review.LikeCount,
                x.Review.CreatedAt,
                x.Review.UpdatedAt,
                new ProfileNovel(x.Novel.Id, x.Novel.Slug, x.Novel.Title, x.Novel.CoverImageUrl)))
            .ToListAsync(cancellationToken);
        return (items, totalCount);
    }

    public async Task<(IReadOnlyList<ProfileComment> Items, int TotalCount)> GetCommentsAsync(string userId, int pageNumber,
        int pageSize, CancellationToken cancellationToken = default)
    {
        var listed = Comments(userId);
        var totalCount = await listed.CountAsync(cancellationToken);
        var items = await listed
            .OrderByDescending(x => x.Comment.CreatedAt)
            .ThenBy(x => x.Comment.Id)
            .Skip((pageNumber - 1) * pageSize)
            .Take(pageSize)
            .Select(x => new ProfileComment(
                x.Comment.Id,
                x.Comment.Content,
                x.Comment.AttachedImageUrl,
                x.Comment.LikesCount,
                x.Comment.CreatedAt,
                x.Comment.UpdatedAt,
                x.Comment.ParentCommentId,
                new ProfileNovel(x.Novel.Id, x.Novel.Slug, x.Novel.Title, x.Novel.CoverImageUrl),
                // Its number among the published chapters, in the reader's order (ChapterIndex).
                new ProfileChapter(x.Chapter.Id, x.Chapter.Title, db.Chapters.Count(other =>
                    other.NovelId == x.Chapter.NovelId
                    && other.Status == ChapterStatuses.Published
                    && other.ChapterIndex < x.Chapter.ChapterIndex) + 1),
                x.Comment.ParagraphId))
            .ToListAsync(cancellationToken);
        return (items, totalCount);
    }

    public async Task<ProfileCounts> CountAsync(string userId, CancellationToken cancellationToken = default) =>
        new(await Reviews(userId).CountAsync(cancellationToken), await Comments(userId).CountAsync(cancellationToken));

    /// <summary>
    /// The member's reviews on novels readers can open (<see cref="NovelFilters.Readable"/>: not a draft, with a
    /// published chapter). The query filter leaves out deleted novels: deleted by their author, removed by a
    /// moderator, or hidden with their author's account. A review has no soft delete: one its author deleted, or a
    /// moderator removed, is gone.
    /// </summary>
    private IQueryable<ReviewRow> Reviews(string userId) =>
        from review in db.Reviews
        where review.ReviewerId == userId
        join novel in db.Novels.Readable() on review.NovelId equals novel.Id
        select new ReviewRow { Review = review, Novel = novel };

    /// <summary>
    /// The member's comments on chapters and paragraphs, top-level and replies, where a reader can read them: not
    /// deleted (the query filter), on a published chapter (a paragraph's, for a paragraph comment) of a novel readers
    /// can open, and, for a reply, under a parent that isn't deleted (the thread is gone with it). Comments on posts,
    /// and replies under them, are not listed. A comment a moderator removed, or on a chapter or paragraph since
    /// deleted, is gone with its replies.
    /// </summary>
    private IQueryable<CommentRow> Comments(string userId) =>
        from comment in db.Comments
        where comment.UserId == userId && comment.PostId == null
        where comment.ParentCommentId == null || db.Comments.Any(parent => parent.Id == comment.ParentCommentId)
        // A reply is stored where its parent is, so this is its thread's chapter too.
        join chapter in db.Chapters
            on comment.ParagraphId != null ? comment.Paragraph!.ChapterId : comment.ChapterId equals (Guid?)chapter.Id
        where chapter.Status == ChapterStatuses.Published
        join novel in db.Novels.Readable() on chapter.NovelId equals novel.Id
        select new CommentRow { Comment = comment, Chapter = chapter, Novel = novel };

    private sealed class ReviewRow
    {
        public Review Review { get; init; } = default!;
        public Novel Novel { get; init; } = default!;
    }

    private sealed class CommentRow
    {
        public Comments Comment { get; init; } = default!;
        public Chapter Chapter { get; init; } = default!;
        public Novel Novel { get; init; } = default!;
    }
}
