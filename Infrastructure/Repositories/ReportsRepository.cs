using Domain.Entities;
using Domain.Moderation;
using Domain.Repositories;
using Infrastructure.Persistence;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Repositories;

public class ReportsRepository(ApplicationDbContext dbContext) : IReportsRepository
{
    public async Task<ReportTarget?> FindTargetAsync(ReportTargetType type, Guid targetId, string viewerId, CancellationToken cancellationToken = default)
    {
        // The query filters leave out deleted comments, posts and novels.
        switch (type)
        {
            case ReportTargetType.Comment:
                var comment = await dbContext.Comments.AsNoTracking()
                    .Where(c => c.Id == targetId)
                    .Select(c => new { c.UserId, c.Content })
                    .FirstOrDefaultAsync(cancellationToken);
                return comment is null ? null : new ReportTarget(comment.UserId, Excerpt(comment.Content));

            case ReportTargetType.Review:
                var review = await dbContext.Reviews.AsNoTracking()
                    .Where(r => r.Id == targetId)
                    .Select(r => new { r.ReviewerId, r.Content })
                    .FirstOrDefaultAsync(cancellationToken);
                return review is null ? null : new ReportTarget(review.ReviewerId, Excerpt(review.Content));

            case ReportTargetType.Post:
                var post = await dbContext.Posts.AsNoTracking()
                    .Where(p => p.Id == targetId)
                    .Select(p => new { p.UserId, p.Content })
                    .FirstOrDefaultAsync(cancellationToken);
                return post is null ? null : new ReportTarget(post.UserId, Excerpt(post.Content));

            case ReportTargetType.User:
                var userId = targetId.ToString();
                // A deleted account has no profile to report.
                var user = await dbContext.Users.AsNoTracking()
                    .Where(u => u.Id == userId && u.DeletedAt == null)
                    .Select(u => new { u.Id, u.UserName, u.DisplayName })
                    .FirstOrDefaultAsync(cancellationToken);
                return user is null ? null : new ReportTarget(user.Id, Excerpt($"{user.DisplayName} (@{user.UserName})"));

            case ReportTargetType.Novel:
                // A draft is only visible to its author.
                var novel = await dbContext.Novels.AsNoTracking()
                    .Where(n => n.Id == targetId && (!n.IsDraft || n.AuthorId == viewerId))
                    .Select(n => new { n.AuthorId, n.Title })
                    .FirstOrDefaultAsync(cancellationToken);
                return novel is null ? null : new ReportTarget(novel.AuthorId, Excerpt(novel.Title));

            case ReportTargetType.ReadingList:
                // A private list is only visible to its owner.
                var list = await dbContext.ReadingLists.AsNoTracking()
                    .Where(rl => rl.Id == targetId && (rl.IsPublic || rl.UserId == viewerId))
                    .Select(rl => new { rl.UserId, rl.Name, rl.Description })
                    .FirstOrDefaultAsync(cancellationToken);
                return list is null
                    ? null
                    : new ReportTarget(list.UserId, Excerpt(string.IsNullOrWhiteSpace(list.Description) ? list.Name : $"{list.Name}: {list.Description}"));

            case ReportTargetType.GiftMessage:
                // A gift's message (#31), whoever can see it; a gift without one (or whose message was removed) has
                // nothing to report.
                var gift = await dbContext.GiftTransactions.AsNoTracking()
                    .Where(t => t.Id == targetId && t.Message != null)
                    .Select(t => new { t.SenderId, t.Message })
                    .FirstOrDefaultAsync(cancellationToken);
                return gift is null ? null : new ReportTarget(gift.SenderId, Excerpt(gift.Message));

            default:
                return null;
        }
    }

    public Task<Report?> GetOpenReportAsync(string reporterId, ReportTargetType type, Guid targetId, CancellationToken cancellationToken = default) =>
        dbContext.Reports.AsNoTracking()
            .FirstOrDefaultAsync(r => r.ReporterId == reporterId && r.TargetType == type && r.TargetId == targetId
                                      && r.Status == ReportStatus.Open, cancellationToken);

    public Task<int> CountCreatedSinceAsync(string reporterId, DateTime since, CancellationToken cancellationToken = default) =>
        dbContext.Reports.CountAsync(r => r.ReporterId == reporterId && r.CreatedAt >= since, cancellationToken);

    public async Task<(Report Report, bool Created)> AddAsync(Report report, CancellationToken cancellationToken = default)
    {
        dbContext.Reports.Add(report);
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
            return (report, true);
        }
        catch (DbUpdateException ex) when (IsDuplicateKey(ex))
        {
            // IX_Reports_Reporter_Target_Open: a concurrent request of the same reporter got there first.
            dbContext.Entry(report).State = EntityState.Detached;
            var existing = await GetOpenReportAsync(report.ReporterId, report.TargetType, report.TargetId, cancellationToken);
            if (existing is null)
            {
                throw;
            }
            return (existing, false);
        }
    }

    private static bool IsDuplicateKey(Exception ex) =>
        (ex as SqlException ?? ex.InnerException as SqlException)?.Number is 2601 or 2627;

    public Task<Report?> GetByIdAsync(Guid reportId, CancellationToken cancellationToken = default) =>
        dbContext.Reports.AsNoTracking().FirstOrDefaultAsync(r => r.Id == reportId, cancellationToken);

    public async Task<(IReadOnlyList<Report> Reports, int TotalCount)> GetPageAsync(
        ReportStatus? status, int pageNumber, int pageSize, CancellationToken cancellationToken = default)
    {
        var query = dbContext.Reports.AsNoTracking();
        if (status is { } wanted)
        {
            query = query.Where(r => r.Status == wanted);
        }

        var totalCount = await query.CountAsync(cancellationToken);

        // The open queue is worked oldest first; closed reports are history, newest first.
        var ordered = status == ReportStatus.Open
            ? query.OrderBy(r => r.CreatedAt).ThenBy(r => r.Id)
            : query.OrderByDescending(r => r.CreatedAt).ThenBy(r => r.Id);

        var reports = await ordered
            .Skip((pageNumber - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);
        return (reports, totalCount);
    }

    public async Task<Dictionary<(ReportTargetType Type, Guid Id), ReportTargetState>> DescribeTargetsAsync(
        IReadOnlyCollection<(ReportTargetType Type, Guid Id)> targets, CancellationToken cancellationToken = default)
    {
        var states = new Dictionary<(ReportTargetType, Guid), ReportTargetState>();
        List<Guid> IdsOf(ReportTargetType type) => targets.Where(t => t.Type == type).Select(t => t.Id).Distinct().ToList();

        // One query per kind of target on the page. Deleted items are read too (soft-deleted ones count as gone), and
        // the query filters are off so that a comment on a deleted novel still finds its novel's slug.
        var commentIds = IdsOf(ReportTargetType.Comment);
        if (commentIds.Count > 0)
        {
            var comments = await dbContext.Comments.IgnoreQueryFilters().AsNoTracking()
                .Where(c => commentIds.Contains(c.Id))
                .Select(c => new
                {
                    c.Id,
                    c.UserId,
                    c.Content,
                    c.IsDeleted,
                    PostOwner = c.PostId != null ? c.Post!.User.UserName : null,
                    ChapterId = c.ParagraphId != null ? (Guid?)c.Paragraph!.ChapterId : c.ChapterId,
                    NovelSlug = c.ParagraphId != null ? c.Paragraph!.Chapter.Novel.Slug
                        : c.ChapterId != null ? c.Chapter!.Novel.Slug
                        : null
                })
                .ToListAsync(cancellationToken);
            foreach (var c in comments.Where(c => !c.IsDeleted))
            {
                var link = c.PostOwner is not null ? $"/profile/{c.PostOwner}"
                    : c.NovelSlug is not null && c.ChapterId is not null ? $"/novel/{c.NovelSlug}/chapter/{c.ChapterId}"
                    : null;
                states[(ReportTargetType.Comment, c.Id)] = new ReportTargetState(true, c.UserId, Excerpt(c.Content), link);
            }
        }

        var reviewIds = IdsOf(ReportTargetType.Review);
        if (reviewIds.Count > 0)
        {
            var reviews = await dbContext.Reviews.IgnoreQueryFilters().AsNoTracking()
                .Where(r => reviewIds.Contains(r.Id))
                .Select(r => new { r.Id, r.ReviewerId, r.Content, r.ReviewedNovel.Slug })
                .ToListAsync(cancellationToken);
            foreach (var r in reviews)
            {
                states[(ReportTargetType.Review, r.Id)] = new ReportTargetState(true, r.ReviewerId, Excerpt(r.Content), $"/novel/{r.Slug}");
            }
        }

        var postIds = IdsOf(ReportTargetType.Post);
        if (postIds.Count > 0)
        {
            var posts = await dbContext.Posts.IgnoreQueryFilters().AsNoTracking()
                .Where(p => postIds.Contains(p.Id) && !p.IsDeleted)
                .Select(p => new { p.Id, p.UserId, p.Content, p.User.UserName })
                .ToListAsync(cancellationToken);
            foreach (var p in posts)
            {
                states[(ReportTargetType.Post, p.Id)] = new ReportTargetState(true, p.UserId, Excerpt(p.Content), $"/profile/{p.UserName}");
            }
        }

        var userIds = IdsOf(ReportTargetType.User).Select(id => id.ToString()).ToList();
        if (userIds.Count > 0)
        {
            // A deleted account counts as gone, like deleted content.
            var users = await dbContext.Users.AsNoTracking()
                .Where(u => userIds.Contains(u.Id) && u.DeletedAt == null)
                .Select(u => new { u.Id, u.UserName, u.DisplayName })
                .ToListAsync(cancellationToken);
            foreach (var u in users)
            {
                if (Guid.TryParse(u.Id, out var id))
                {
                    states[(ReportTargetType.User, id)] = new ReportTargetState(
                        true, u.Id, Excerpt($"{u.DisplayName} (@{u.UserName})"), $"/profile/{u.UserName}");
                }
            }
        }

        var novelIds = IdsOf(ReportTargetType.Novel);
        if (novelIds.Count > 0)
        {
            var novels = await dbContext.Novels.IgnoreQueryFilters().AsNoTracking()
                .Where(n => novelIds.Contains(n.Id) && !n.IsDeleted)
                .Select(n => new { n.Id, n.AuthorId, n.Title, n.Slug })
                .ToListAsync(cancellationToken);
            foreach (var n in novels)
            {
                states[(ReportTargetType.Novel, n.Id)] = new ReportTargetState(true, n.AuthorId, Excerpt(n.Title), $"/novel/{n.Slug}");
            }
        }

        var listIds = IdsOf(ReportTargetType.ReadingList);
        if (listIds.Count > 0)
        {
            var lists = await dbContext.ReadingLists.AsNoTracking()
                .Where(rl => listIds.Contains(rl.Id))
                .Select(rl => new { rl.Id, rl.UserId, rl.Name })
                .ToListAsync(cancellationToken);
            foreach (var rl in lists)
            {
                states[(ReportTargetType.ReadingList, rl.Id)] = new ReportTargetState(true, rl.UserId, Excerpt(rl.Name), $"/reading-list/{rl.Id}");
            }
        }

        var giftIds = IdsOf(ReportTargetType.GiftMessage);
        if (giftIds.Count > 0)
        {
            // Gone once removed (the gift stays, without its message). Shown under the novel's recent gifts.
            var gifts = await dbContext.GiftTransactions.IgnoreQueryFilters().AsNoTracking()
                .Where(t => giftIds.Contains(t.Id) && t.Message != null)
                .Select(t => new { t.Id, t.SenderId, t.Message, t.Novel.Slug })
                .ToListAsync(cancellationToken);
            foreach (var t in gifts)
            {
                states[(ReportTargetType.GiftMessage, t.Id)] = new ReportTargetState(true, t.SenderId, Excerpt(t.Message), $"/novel/{t.Slug}");
            }
        }

        foreach (var target in targets)
        {
            states.TryAdd(target, ReportTargetState.Gone);
        }
        return states;
    }

    public async Task<Dictionary<(ReportTargetType Type, Guid Id), int>> CountOpenReportsAsync(
        IReadOnlyCollection<(ReportTargetType Type, Guid Id)> targets, CancellationToken cancellationToken = default)
    {
        var ids = targets.Select(t => t.Id).Distinct().ToList();
        if (ids.Count == 0)
        {
            return [];
        }

        var counts = await dbContext.Reports.AsNoTracking()
            .Where(r => r.Status == ReportStatus.Open && ids.Contains(r.TargetId))
            .GroupBy(r => new { r.TargetType, r.TargetId })
            .Select(g => new { g.Key.TargetType, g.Key.TargetId, Count = g.Count() })
            .ToListAsync(cancellationToken);
        return counts.ToDictionary(c => (c.TargetType, c.TargetId), c => c.Count);
    }

    public Task<int> ResolveOpenReportsAsync(ReportTargetType type, Guid targetId, ReportStatus status, ReportAction action,
        string adminId, DateTime resolvedAt, CancellationToken cancellationToken = default) =>
        dbContext.Reports
            .Where(r => r.TargetType == type && r.TargetId == targetId && r.Status == ReportStatus.Open)
            .ExecuteUpdateAsync(s => s
                .SetProperty(r => r.Status, status)
                .SetProperty(r => r.Resolution, (ReportAction?)action)
                .SetProperty(r => r.ResolvedAt, (DateTime?)resolvedAt)
                .SetProperty(r => r.ResolvedById, adminId), cancellationToken);

    /// <summary>The text as a report keeps it: trimmed, and cut to <see cref="Report.ExcerptMaxLength"/>.</summary>
    internal static string? Excerpt(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }
        var trimmed = text.Trim();
        if (trimmed.Length <= Report.ExcerptMaxLength)
        {
            return trimmed;
        }
        var cut = Report.ExcerptMaxLength - 1;
        if (char.IsHighSurrogate(trimmed[cut - 1]))
        {
            cut--; // don't split an emoji
        }
        return trimmed[..cut] + "…";
    }
}
