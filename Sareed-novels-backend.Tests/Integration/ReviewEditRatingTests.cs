using Application.Search.DTOs;
using Domain.Entities;
using Domain.Reviews;
using Infrastructure.Repositories;
using Infrastructure.Services;
using Infrastructure.Services.Search;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace Sareed_novels_backend.Tests.Integration;

/// <summary>
/// An edited score reaches everything that reads review scores (#34), through the repository methods the API uses:
/// the novel's stored averages (the novel page, novel lists and search) and each review's own average (rankings).
/// </summary>
public class ReviewEditRatingTests(SqlServerDatabase database) : IClassFixture<SqlServerDatabase>
{
    private static Review NewReview(User reviewer, Novel novel, decimal score)
    {
        var review = new Review
        {
            Id = Guid.NewGuid(), ReviewerId = reviewer.Id, NovelId = novel.Id, Content = "مراجعة", CreatedAt = DateTime.UtcNow,
            WritingQualityScore = score, UpdatingStabilityScore = score, CharacterDevelopmentScore = score, WorldBuildingScore = score
        };
        review.CalculateAverageScore();
        return review;
    }

    /// <summary>Written as the API writes a review.</summary>
    private async Task<Review> Create(User reviewer, Novel novel, decimal score)
    {
        var review = NewReview(reviewer, novel, score);
        await using var db = database.CreateContext();
        Assert.True(await new ReviewsRepository(db).CreateOne(review));
        return review;
    }

    private async Task AssertRatings(Novel novel, string marker, DateTime now, decimal novelAverage, double rankingQuality)
    {
        await using var db = database.CreateContext();

        var stored = await db.Novels.AsNoTracking().SingleAsync(n => n.Id == novel.Id);
        Assert.Equal((novelAverage, 2), (stored.TotalAverageScore, stored.ReviewCount));
        var found = Assert.Single((await new NovelSearchService(db).SearchNovelsAsync(new SearchNovelsRequest { Query = marker })).Items);
        Assert.Equal((novelAverage, 2), (found.TotalAverageScore, found.ReviewCount));

        // Rankings read each review's own average (from reviewers who read the novel), smoothed toward the site's average
        // of them; here these two reviews are all there is, so the smoothed rating is their average.
        var lists = await new RankingService(db, new FixedTimeProvider(now), NullLogger<RankingService>.Instance).ComputeListsAsync(now);
        var entry = Assert.Single(lists.Single(l => l.Name == "TrendingNow").Entries, e => e.NovelId == novel.Id);
        Assert.Equal(rankingQuality, entry.Quality, 6);
    }

    [Fact]
    public async Task Search_and_rankings_read_an_edited_score()
    {
        var now = DateTime.UtcNow;
        var marker = Seed.Marker();
        var author = Seed.User();
        var (reader, other) = (Seed.User(), Seed.User());
        var novel = Seed.Novel(author, "رواية " + marker);
        var chapters = Seed.Chapters(novel, 3, now.AddDays(-10));
        await using (var db = database.CreateContext())
        {
            db.Users.AddRange(author, reader, other);
            db.Novels.Add(novel);
            db.Chapters.AddRange(chapters);
            // Both reviewers read to the last chapter, so rankings trust their scores.
            db.UserNovelProgress.AddRange(Seed.Progress(reader, chapters[2], 3, now.AddDays(-1)), Seed.Progress(other, chapters[2], 3, now.AddDays(-1)));
            await db.SaveChangesAsync();
        }
        var review = await Create(reader, novel, 2);
        await Create(other, novel, 4);
        await AssertRatings(novel, marker, now, novelAverage: 3, rankingQuality: 3);

        await using (var db = database.CreateContext())
        {
            Assert.True(await new ReviewsRepository(db).UpdateReview(review, ReviewEdit.Of(5, 5, 5, 5, null, null), now));
        }

        await AssertRatings(novel, marker, now, novelAverage: 4.5m, rankingQuality: 4.5);
    }

    [Fact]
    public async Task Any_scores_sent_leave_the_review_the_average_of_its_four_scores()
    {
        var author = Seed.User();
        var reader = Seed.User();
        var novel = Seed.Novel(author, "رواية " + Seed.Marker());
        await using (var db = database.CreateContext())
        {
            db.Users.AddRange(author, reader);
            db.Novels.Add(novel);
            await db.SaveChangesAsync();
        }
        var review = await Create(reader, novel, 1);

        // Every set of scores an edit can send, at 5 (two or more of them add up to more than a score column holds) and
        // then at 1.5, so each edit changes what it sends and leaves the rest as the edits before it left them.
        var expected = new[] { 1m, 1m, 1m, 1m };
        for (var sent = 1; sent < 16; sent++)
        {
            foreach (var value in new[] { 5m, 1.5m })
            {
                var scores = Enumerable.Range(0, 4).Select(i => (sent & (1 << i)) != 0 ? value : (decimal?)null).ToArray();
                expected = expected.Zip(scores, (before, score) => score ?? before).ToArray();
                await using var db = database.CreateContext();

                Assert.True(await new ReviewsRepository(db).UpdateReview(review, ReviewEdit.Of(scores[0], scores[1], scores[2], scores[3], null, null), DateTime.UtcNow));

                var stored = await db.Reviews.AsNoTracking().SingleAsync(r => r.Id == review.Id);
                Assert.Equal(expected, new[] { stored.WritingQualityScore, stored.UpdatingStabilityScore, stored.CharacterDevelopmentScore, stored.WorldBuildingScore });
                // As SQL Server stores 4.125 in a decimal(3,2) column, and as creating a review stores it: 4.13.
                Assert.Equal(Math.Round(expected.Sum() / 4, 2, MidpointRounding.AwayFromZero), stored.TotalAverageScore);
                var stats = await db.Novels.AsNoTracking().SingleAsync(n => n.Id == novel.Id);
                Assert.Equal((stored.TotalAverageScore, expected[0], expected[3], 1),
                    (stats.TotalAverageScore, stats.AverageWritingQualityScore, stats.AverageWorldBuildingScore, stats.ReviewCount));
            }
        }
    }

    [Fact]
    public async Task Concurrent_partial_edits_leave_the_average_of_the_scores_they_leave()
    {
        var author = Seed.User();
        var reader = Seed.User();
        var novel = Seed.Novel(author, "رواية " + Seed.Marker());
        await using (var db = database.CreateContext())
        {
            db.Users.AddRange(author, reader);
            db.Novels.Add(novel);
            await db.SaveChangesAsync();
        }
        var review = await Create(reader, novel, 1);

        // Each edit sends one score; together they race on the one row.
        var edits = Enumerable.Range(0, 12).Select(i => (i % 4) switch
        {
            0 => ReviewEdit.Of(2 + i % 3, null, null, null, null, null),
            1 => ReviewEdit.Of(null, 2 + i % 3, null, null, null, null),
            2 => ReviewEdit.Of(null, null, 2 + i % 3, null, null, null),
            _ => ReviewEdit.Of(null, null, null, 2 + i % 3, null, null)
        });
        await Task.WhenAll(edits.Select(async edit =>
        {
            await using var db = database.CreateContext();
            Assert.True(await new ReviewsRepository(db).UpdateReview(review, edit, DateTime.UtcNow));
        }));

        await using (var db = database.CreateContext())
        {
            var stored = await db.Reviews.AsNoTracking().SingleAsync(r => r.Id == review.Id);
            Assert.All(new[] { stored.WritingQualityScore, stored.UpdatingStabilityScore, stored.CharacterDevelopmentScore, stored.WorldBuildingScore },
                score => Assert.InRange(score, 2m, 4m));
            Assert.Equal(
                (stored.WritingQualityScore + stored.UpdatingStabilityScore + stored.CharacterDevelopmentScore + stored.WorldBuildingScore) / 4,
                stored.TotalAverageScore);
            // Once the edits are done, the novel's stats are those of its one review.
            var stats = await db.Novels.AsNoTracking().SingleAsync(n => n.Id == novel.Id);
            Assert.Equal((stored.TotalAverageScore, 1), (stats.TotalAverageScore, stats.ReviewCount));
        }
    }
}
