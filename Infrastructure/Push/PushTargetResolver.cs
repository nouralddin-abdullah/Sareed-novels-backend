using Domain.Constants;
using Domain.Entities;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Push;

/// <summary>
/// Finds the ids behind a batch of notifications (<see cref="PushTarget"/>) with a few set-based queries: a
/// notification only stores its actor and one related entity (see NotificationService), e.g. a reply's comment id,
/// from which this gets the thread, chapter and novel or the post and its author.
/// </summary>
public sealed class PushTargetResolver(ApplicationDbContext dbContext)
{
    public async Task<Dictionary<Guid, PushTarget>> ResolveAsync(IReadOnlyCollection<Notification> notifications, CancellationToken cancellationToken)
    {
        List<Guid> Related(params string[] types) => notifications
            .Where(n => n.RelatedEntityId.HasValue && n.RelatedEntityType is { } type && types.Contains(type))
            .Select(n => n.RelatedEntityId!.Value)
            .Distinct()
            .ToList();

        // Deleted comments and novels still say where they were.
        var commentIds = Related("Comment");
        var comments = commentIds.Count == 0 ? new Dictionary<Guid, CommentPlace>() : await dbContext.Comments
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(c => commentIds.Contains(c.Id))
            .Select(c => new CommentPlace(c.Id, c.ParentCommentId, c.ChapterId ?? (Guid?)c.Paragraph!.ChapterId, c.ParagraphId, c.PostId))
            .ToDictionaryAsync(c => c.Id, cancellationToken);

        var reviewIds = Related("Review");
        var reviewNovels = reviewIds.Count == 0 ? new Dictionary<Guid, Guid>() : await dbContext.Reviews
            .AsNoTracking()
            .Where(r => reviewIds.Contains(r.Id))
            .Select(r => new { r.Id, r.NovelId })
            .ToDictionaryAsync(r => r.Id, r => r.NovelId, cancellationToken);

        var chapterIds = Related("Chapter")
            .Concat(comments.Values.Select(c => c.ChapterId).OfType<Guid>())
            .Distinct()
            .ToList();
        var chapterNovels = chapterIds.Count == 0 ? new Dictionary<Guid, Guid>() : await dbContext.Chapters
            .AsNoTracking()
            .Where(c => chapterIds.Contains(c.Id))
            .Select(c => new { c.Id, c.NovelId })
            .ToDictionaryAsync(c => c.Id, c => c.NovelId, cancellationToken);

        // Gift and privilege notifications point at the novel itself.
        var novelIds = Related("Gift", "Privilege")
            .Concat(chapterNovels.Values)
            .Concat(reviewNovels.Values)
            .Distinct()
            .ToList();
        var novelSlugs = novelIds.Count == 0 ? new Dictionary<Guid, string>() : await dbContext.Novels
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(n => novelIds.Contains(n.Id))
            .Select(n => new { n.Id, n.Slug })
            .ToDictionaryAsync(n => n.Id, n => n.Slug, cancellationToken);

        // A post's author, for a post or a comment on one: the app opens a post from its author's profile. Deleted posts
        // still say whose they were.
        var postIds = Related("Post")
            .Concat(comments.Values.Select(c => c.PostId).OfType<Guid>())
            .Distinct()
            .ToList();
        var postAuthors = postIds.Count == 0 ? new Dictionary<Guid, string?>() : await dbContext.Posts
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(p => postIds.Contains(p.Id))
            .Select(p => new { p.Id, p.User.UserName })
            .ToDictionaryAsync(p => p.Id, p => p.UserName, cancellationToken);

        // What a gift's sender wrote (#31), read from the gift record now: a message a moderator removed is not pushed.
        var giftTransactionIds = notifications
            .Where(n => n.Type == NotificationType.GiftReceived && n.GiftTransactionId.HasValue)
            .Select(n => n.GiftTransactionId!.Value)
            .Distinct()
            .ToList();
        var giftMessages = giftTransactionIds.Count == 0 ? new Dictionary<Guid, string>() : await dbContext.GiftTransactions
            .AsNoTracking()
            .Where(t => giftTransactionIds.Contains(t.Id) && t.Message != null)
            .ToDictionaryAsync(t => t.Id, t => t.Message!, cancellationToken);

        // The actor is a user, except for new chapters (the novel).
        var actorIds = notifications.Select(n => n.ActorId).Distinct().ToList();
        var userNames = await dbContext.Users
            .AsNoTracking()
            .Where(u => actorIds.Contains(u.Id))
            .Select(u => new { u.Id, u.UserName })
            .ToDictionaryAsync(u => u.Id, u => u.UserName, cancellationToken);

        var targets = new Dictionary<Guid, PushTarget>();
        foreach (var n in notifications)
        {
            var target = new PushTarget { ActorUserName = userNames.GetValueOrDefault(n.ActorId) };
            var related = n.RelatedEntityId;
            switch (n.RelatedEntityType)
            {
                case "Comment":
                    var place = related is { } commentId ? comments.GetValueOrDefault(commentId) : null;
                    target = target with
                    {
                        CommentId = related,
                        ParentCommentId = place?.ParentCommentId,
                        ChapterId = place?.ChapterId,
                        ParagraphId = place?.ParagraphId,
                        PostId = place?.PostId
                    };
                    break;
                case "Chapter":
                    target = target with { ChapterId = related };
                    break;
                case "Review":
                    target = target with
                    {
                        ReviewId = related,
                        NovelId = related is { } reviewId && reviewNovels.TryGetValue(reviewId, out var reviewedNovel) ? reviewedNovel : null
                    };
                    break;
                case "Post":
                    target = target with { PostId = related };
                    break;
                case "ReadingList":
                    target = target with { ReadingListId = related };
                    break;
                case "Gift" or "Privilege":
                    target = target with { NovelId = related };
                    break;
            }

            if (target.NovelId is null && target.ChapterId is { } chapterId && chapterNovels.TryGetValue(chapterId, out var novelId))
            {
                target = target with { NovelId = novelId };
            }
            if (target.NovelId is { } id)
            {
                target = target with { NovelSlug = novelSlugs.GetValueOrDefault(id) };
            }
            if (target.PostId is { } postId)
            {
                target = target with { PostAuthorUserName = postAuthors.GetValueOrDefault(postId) };
            }
            if (n.GiftTransactionId is { } giftTransactionId)
            {
                target = target with { GiftMessage = giftMessages.GetValueOrDefault(giftTransactionId) };
            }
            targets[n.Id] = target;
        }
        return targets;
    }

    private sealed record CommentPlace(Guid Id, Guid? ParentCommentId, Guid? ChapterId, Guid? ParagraphId, Guid? PostId);
}
