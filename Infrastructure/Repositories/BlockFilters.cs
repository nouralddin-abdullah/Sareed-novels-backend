using Domain.Entities;
using Infrastructure.Persistence;

namespace Infrastructure.Repositories;

/// <summary>
/// What a signed-in viewer's lists leave out: comments, replies and reviews written by users the viewer blocked. The
/// filter is a NOT EXISTS subquery in the list's own query (a seek on the UserBlocks key per row), so it costs no
/// extra round trip; anonymous viewers (null) see everything.
/// </summary>
internal static class BlockFilters
{
    public static IQueryable<Comments> VisibleTo(this IQueryable<Comments> comments, ApplicationDbContext db, string? viewerId) =>
        viewerId is null
            ? comments
            : comments.Where(c => !db.UserBlocks.Any(b => b.BlockerId == viewerId && b.BlockedId == c.UserId));

    public static IQueryable<Review> VisibleTo(this IQueryable<Review> reviews, ApplicationDbContext db, string? viewerId) =>
        viewerId is null
            ? reviews
            : reviews.Where(r => !db.UserBlocks.Any(b => b.BlockerId == viewerId && b.BlockedId == r.ReviewerId));
}
