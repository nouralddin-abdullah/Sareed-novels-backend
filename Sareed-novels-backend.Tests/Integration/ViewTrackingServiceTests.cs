using Domain.Entities;
using Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace Sareed_novels_backend.Tests.Integration;

public class ViewTrackingServiceTests(SqlServerDatabase database) : IClassFixture<SqlServerDatabase>
{
    private async Task<(Novel Novel, Chapter Chapter)> SeedNovel()
    {
        await using var db = database.CreateContext();
        var author = Seed.User();
        var novel = Seed.Novel(author, "رواية " + Seed.Marker());
        var chapter = Seed.Chapters(novel, 1, DateTime.UtcNow.AddDays(-1)).Single();
        db.Users.Add(author);
        db.Novels.Add(novel);
        db.Chapters.Add(chapter);
        await db.SaveChangesAsync();
        return (novel, chapter);
    }

    private ViewTrackingService Service(Infrastructure.Persistence.ApplicationDbContext db) =>
        new(db, NullLogger<ViewTrackingService>.Instance);

    [Fact]
    public async Task A_visitor_counts_once_per_day_however_often_they_reload()
    {
        var (novel, _) = await SeedNovel();

        for (var i = 0; i < 5; i++)
        {
            await using var db = database.CreateContext();
            await Service(db).TrackNovelView(novel.Id, "a:same-visitor");
        }

        await using var check = database.CreateContext();
        var today = DateTime.UtcNow.Date;
        Assert.Equal(1, await check.NovelViews.Where(v => v.NovelId == novel.Id && v.ViewDate == today).SumAsync(v => v.ViewCount));
        Assert.Equal(1, (await check.Novels.SingleAsync(n => n.Id == novel.Id)).TotalViews);
    }

    [Fact]
    public async Task Different_visitors_each_count()
    {
        var (novel, _) = await SeedNovel();

        await using (var db = database.CreateContext())
        {
            await Service(db).TrackNovelView(novel.Id, "a:first");
            await Service(db).TrackNovelView(novel.Id, "u:second");
            await Service(db).TrackNovelView(novel.Id, "a:first");
        }

        await using var check = database.CreateContext();
        Assert.Equal(2, (await check.Novels.SingleAsync(n => n.Id == novel.Id)).TotalViews);
        Assert.Equal(2, await check.DailyUniqueViews.CountAsync(v => v.NovelId == novel.Id && v.Kind == ViewKind.NovelPage));
    }

    [Fact]
    public async Task Concurrent_first_views_do_not_lose_or_double_count()
    {
        var (novel, _) = await SeedNovel();

        await Task.WhenAll(Enumerable.Range(0, 20).Select(async i =>
        {
            await using var db = database.CreateContext();
            await Service(db).TrackNovelView(novel.Id, $"a:visitor-{i % 10}");
        }));

        await using var check = database.CreateContext();
        Assert.Equal(10, (await check.Novels.SingleAsync(n => n.Id == novel.Id)).TotalViews);
        Assert.Equal(10, await check.NovelViews.Where(v => v.NovelId == novel.Id).SumAsync(v => v.ViewCount));
    }

    [Fact]
    public async Task Chapter_reads_are_deduplicated_per_visitor_per_day()
    {
        var (novel, chapter) = await SeedNovel();

        await using (var db = database.CreateContext())
        {
            await Service(db).TrackChapterView(chapter.Id, novel.Id, "a:reader");
            await Service(db).TrackChapterView(chapter.Id, novel.Id, "a:reader");
            await Service(db).TrackChapterView(chapter.Id, novel.Id, "u:other");
        }

        await using var check = database.CreateContext();
        Assert.Equal(2, (await check.Chapters.SingleAsync(c => c.Id == chapter.Id)).ViewsCount);
    }

    [Fact]
    public async Task Pruning_removes_only_old_visitor_rows()
    {
        var (novel, _) = await SeedNovel();
        await using (var db = database.CreateContext())
        {
            db.DailyUniqueViews.AddRange(
                new DailyUniqueView { TargetId = novel.Id, NovelId = novel.Id, Day = DateTime.UtcNow.Date.AddDays(-200), VisitorKey = "a:old", Kind = ViewKind.NovelPage },
                new DailyUniqueView { TargetId = novel.Id, NovelId = novel.Id, Day = DateTime.UtcNow.Date.AddDays(-1), VisitorKey = "a:recent", Kind = ViewKind.NovelPage });
            await db.SaveChangesAsync();
            await Service(db).PruneUniqueViewsAsync(DateTime.UtcNow.AddDays(-120));
        }

        await using var check = database.CreateContext();
        var remaining = await check.DailyUniqueViews.Where(v => v.NovelId == novel.Id).Select(v => v.VisitorKey).ToListAsync();
        Assert.Equal(new[] { "a:recent" }, remaining);
    }
}
