using Domain.Entities;
using Infrastructure.Repositories;
using Microsoft.EntityFrameworkCore;

namespace Sareed_novels_backend.Tests.Integration;

public class GiftLeaderboardTests(SqlServerDatabase database) : IClassFixture<SqlServerDatabase>
{
    [Fact]
    public async Task Leaderboards_rank_senders_by_points_and_the_weekly_one_only_counts_the_last_seven_days()
    {
        var (steady, recent, oldWhale, author) = (Seed.User(), Seed.User(), Seed.User(), Seed.User());
        var novel = Seed.Novel(author, "رواية " + Seed.Marker());
        var gift = new Gift { Id = Guid.NewGuid(), Name = "Rose", ImageUrl = "https://example.test/rose.png", Cost = 100 };

        GiftTransaction Sent(User sender, int count, int daysAgo) => new()
        {
            Id = Guid.NewGuid(), GiftId = gift.Id, NovelId = novel.Id, SenderId = sender.Id, Count = count,
            TotalCost = 100 * count, CreatedAt = DateTime.UtcNow.AddDays(-daysAgo)
        };

        await using (var db = database.CreateContext())
        {
            db.Users.AddRange(steady, recent, oldWhale, author);
            db.Novels.Add(novel);
            db.Gifts.Add(gift);
            db.GiftTransactions.AddRange(
                Sent(steady, 6, daysAgo: 2), Sent(steady, 4, daysAgo: 5),
                Sent(recent, 5, daysAgo: 0),
                Sent(oldWhale, 50, daysAgo: 10));
            await db.SaveChangesAsync();
        }

        // Twice: rebuilding must replace the board, not append to it.
        for (var i = 0; i < 2; i++)
        {
            await using var db = database.CreateContext();
            var repository = new GlobalSupporterLeaderboardRepository(db);
            await repository.RecalculateWeeklyLeaderboard();
            await repository.RecalculateAllTimeLeaderboard();
        }

        await using var check = database.CreateContext();
        var repositoryForReads = new GlobalSupporterLeaderboardRepository(check);

        var (weekly, weeklyCount) = await repositoryForReads.GetLeaderboard("Weekly", 1, 20);
        Assert.Equal(2, weeklyCount);
        Assert.Equal([(steady.Id, 1000m, 10, 1), (recent.Id, 500m, 5, 2)],
            weekly.Select(r => (r.UserId, r.TotalPointsGifted, r.TotalGiftsCount, r.Rank)));

        var (allTime, allTimeCount) = await repositoryForReads.GetLeaderboard("AllTime", 1, 20);
        Assert.Equal(3, allTimeCount);
        Assert.Equal([oldWhale.Id, steady.Id, recent.Id], allTime.Select(r => r.UserId));
        Assert.Equal([1, 2, 3], allTime.Select(r => r.Rank));
    }
}
