using Infrastructure.Repositories;
using Microsoft.EntityFrameworkCore;

namespace Sareed_novels_backend.Tests.Integration;

public class ChapterCountTests(SqlServerDatabase database) : IClassFixture<SqlServerDatabase>
{
    [Fact]
    public async Task Refreshing_sets_the_count_from_the_chapters_table_and_heals_drift()
    {
        await using (var seed = database.CreateContext())
        {
            var author = Seed.User();
            var novel = Seed.Novel(author, "رواية " + Seed.Marker());
            novel.ChapterCount = -1; // what the cross-novel delete bug left behind
            seed.Users.Add(author);
            seed.Novels.Add(novel);
            seed.Chapters.AddRange(Seed.Chapters(novel, 3, DateTime.UtcNow, status: "Published"));
            seed.Chapters.AddRange(Seed.Chapters(novel, 2, DateTime.UtcNow, status: "Draft", startIndex: 4));
            await seed.SaveChangesAsync();
            novelId = novel.Id;
        }

        var updatedAt = new DateTime(2026, 9, 25, 12, 0, 0, DateTimeKind.Utc);
        await using (var db = database.CreateContext())
        {
            var repository = new NovelsRepository(db);
            var tracked = await repository.GetOne(novelId);

            await repository.RefreshChapterCountAsync(novelId, updatedAt);

            // The tracked copy is refreshed too, so a later SaveChanges cannot write the stale count back.
            Assert.Equal(5, tracked!.ChapterCount);
        }

        await using var check = database.CreateContext();
        var stored = await check.Novels.SingleAsync(n => n.Id == novelId);
        Assert.Equal(5, stored.ChapterCount);
        Assert.Equal(updatedAt, stored.LastUpdatedAt);
    }

    private Guid novelId;
}
