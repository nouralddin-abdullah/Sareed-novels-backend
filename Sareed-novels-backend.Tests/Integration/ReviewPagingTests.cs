using System.Data.SqlTypes;
using Domain.Entities;
using Infrastructure.Repositories;

namespace Sareed_novels_backend.Tests.Integration;

/// <summary>
/// A novel's reviews page by page (#25): every sorting is a total order, so no review repeats or goes missing across
/// pages, even when many have the same like count and date (every new review has 0 likes).
/// </summary>
public class ReviewPagingTests(SqlServerDatabase database) : IClassFixture<SqlServerDatabase>
{
    [Fact]
    public async Task Pages_of_every_sorting_hold_each_review_once_in_a_stable_order()
    {
        var at = new DateTime(2026, 9, 20, 12, 0, 0, DateTimeKind.Utc);
        var author = Seed.User();
        var novel = Seed.Novel(author, "رواية " + Seed.Marker());
        var reviewers = Enumerable.Range(0, 7).Select(_ => Seed.User()).ToList();
        Review ReviewBy(int i, int likes, DateTime createdAt) => new()
        {
            Id = Guid.NewGuid(), ReviewerId = reviewers[i].Id, NovelId = novel.Id, Content = "مراجعة", LikeCount = likes, CreatedAt = createdAt,
            WritingQualityScore = 4, UpdatingStabilityScore = 4, CharacterDevelopmentScore = 4, WorldBuildingScore = 4, TotalAverageScore = 4
        };
        var mostLiked = ReviewBy(0, likes: 2, at);
        var sameMoment = Enumerable.Range(1, 4).Select(i => ReviewBy(i, likes: 0, at)).ToList(); // ties all the way to the id
        var newest = ReviewBy(5, likes: 0, at.AddMinutes(1));
        var oldest = ReviewBy(6, likes: 1, at.AddMinutes(-1));
        await using (var db = database.CreateContext())
        {
            db.Users.Add(author);
            db.Users.AddRange(reviewers);
            db.Novels.Add(novel);
            db.Reviews.AddRange([mostLiked, .. sameMoment, newest, oldest]);
            await db.SaveChangesAsync();
        }

        // SQL Server orders uniqueidentifiers its own way (SqlGuid compares the same).
        var tiedById = sameMoment.Select(r => r.Id).OrderBy(id => new SqlGuid(id)).ToList();
        var atMomentById = sameMoment.Append(mostLiked).Select(r => r.Id).OrderBy(id => new SqlGuid(id)).ToList();
        var expected = new Dictionary<string, List<Guid>>
        {
            // By likes, then newest first, then id; an unknown sorting is the same.
            ["likes"] = [mostLiked.Id, oldest.Id, newest.Id, .. tiedById],
            ["whatever"] = [mostLiked.Id, oldest.Id, newest.Id, .. tiedById],
            ["newest"] = [newest.Id, .. atMomentById, oldest.Id],
            ["oldest"] = [oldest.Id, .. atMomentById, newest.Id],
        };

        foreach (var (sorting, order) in expected)
        {
            foreach (var pageSize in new[] { 1, 2, 3, 7 })
            {
                var paged = new List<Guid>();
                for (var page = 1; paged.Count < order.Count; page++)
                {
                    await using var db = database.CreateContext();
                    var (reviews, total) = await new ReviewsRepository(db).GetNovelReviews(novel.Id, pageSize, page, sorting);
                    Assert.Equal(7, total);
                    var ids = reviews.Select(r => r.Id).ToList();
                    Assert.NotEmpty(ids);
                    paged.AddRange(ids);
                }
                Assert.True(order.SequenceEqual(paged), $"{sorting}, {pageSize} a page: got {string.Join(", ", paged)}");
            }
        }
    }
}
