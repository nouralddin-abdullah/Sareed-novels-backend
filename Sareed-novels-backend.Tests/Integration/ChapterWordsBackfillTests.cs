using System.Data.Common;
using Domain.Constants;
using Domain.Entities;
using Infrastructure.BackgroundJobs;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Sareed_novels_backend.Tests.Integration;

/// <summary>
/// #77: chapters from before word counts (WordsCount null) are counted once, in the background after the app starts
/// (<see cref="ChapterWordsBackfillService"/>), from their stored paragraphs as the API serves them, by the rule creating
/// and saving use (an image's caption counts, a break doesn't; paragraphs from before chapter format v1 too); a chapter
/// that has a count keeps it, a count a save stores meanwhile wins, and running it again changes nothing.
/// </summary>
public class ChapterWordsBackfillTests(SqlServerDatabase database) : IClassFixture<SqlServerDatabase>
{
    private readonly ListLogger<ChapterWordsBackfillService> log = new();

    [Fact]
    public async Task Chapters_without_a_count_are_counted_from_their_stored_paragraphs_once()
    {
        var novel = await SeedNovel();
        var tashkeel = await SeedChapter(novel, null, Text("<p>قَالَ الرَّجُلُ: «مَرْحَبًا!»</p>"), Text("* * *"), Text("<p>ثُمَّ مَضَى ...</p>"));
        var pictures = await SeedChapter(novel, null, Picture("https://files.test/novel-images/a.png"),
            Picture("https://files.test/novel-images/map.png", "خريطة المدينة"), Text("<p>تعليق<br>الصورة</p>"), Break());
        // Stored before chapter format v1: the web editor's markup, and a picture among the text.
        var legacy = await SeedChapter(novel, null, Text("<p class=\"min-h-[1em]\">قبل <img src=\"https://files.test/a.png\"> بعد</p>"));
        var empty = await SeedChapter(novel, null);
        var counted = await SeedChapter(novel, 999, Text("<p>كلمة</p>"));
        // More than a batch.
        var many = new List<Guid>();
        for (var i = 0; i < ChapterWordsBackfillService.BatchSize + 5; i++)
        {
            many.Add(await SeedChapter(novel, null, Text("<p>كلمة واحدة</p>"), Text("<p>وثانية</p>")));
        }

        Assert.True(await Backfill().BackfillAsync(CancellationToken.None) >= 4 + many.Count);

        Assert.Equal((5, 4, 2, 0, 999),
            (await Words(tashkeel), await Words(pictures), await Words(legacy), await Words(empty), await Words(counted)));
        foreach (var chapter in many)
        {
            Assert.Equal(3, await Words(chapter));
        }

        // Again: nothing left to count, nothing changes.
        Assert.Equal(0, await Backfill().BackfillAsync(CancellationToken.None));
        Assert.Equal((5, 999), (await Words(tashkeel), await Words(counted)));
    }

    [Fact]
    public async Task A_count_a_save_stores_meanwhile_is_kept()
    {
        var novel = await SeedNovel();
        var chapter = await SeedChapter(novel, null, Text("<p>نص قديم من كلمات أربع</p>"));

        // The backfill has read the old text; just before it stores its count, the author saves new text with its count.
        var save = new CommandHook();
        save.Before(command => command.CommandText.Contains("[WordsCount] =")
                               && command.Parameters.Cast<DbParameter>().Any(p => chapter.Equals(p.Value)), async () =>
        {
            await using var db = database.CreateContext();
            await db.Chapters.Where(c => c.Id == chapter).ExecuteUpdateAsync(s => s.SetProperty(c => c.WordsCount, 7));
        });
        await Backfill(save).BackfillAsync(CancellationToken.None);

        Assert.Equal(7, await Words(chapter));
    }

    [Fact]
    public async Task It_runs_by_itself_after_the_app_starts_and_says_how_many_it_counted()
    {
        var novel = await SeedNovel();
        var chapter = await SeedChapter(novel, null, Text("<p>فصل قديم</p>"));
        var service = Backfill();

        await service.StartAsync(CancellationToken.None);
        await service.ExecuteTask!.WaitAsync(TimeSpan.FromSeconds(60)); // it ends once every chapter has a count
        await service.StopAsync(CancellationToken.None);

        Assert.Equal(2, await Words(chapter));
        Assert.Contains(log.Entries, e => e.Level == LogLevel.Information && e.Message.StartsWith("Counted the words of"));
        Assert.DoesNotContain(log.Entries, e => e.Level >= LogLevel.Warning);
    }

    private ChapterWordsBackfillService Backfill(params IInterceptor[] interceptors)
    {
        var services = new ServiceCollection()
            .AddDbContext<ApplicationDbContext>(o => o.UseSqlServer(database.ConnectionString).AddInterceptors(interceptors))
            .BuildServiceProvider();
        return new ChapterWordsBackfillService(services.GetRequiredService<IServiceScopeFactory>(), TimeProvider.System, log);
    }

    private async Task<Novel> SeedNovel()
    {
        await using var db = database.CreateContext();
        var author = Seed.User();
        var novel = Seed.Novel(author, "رواية " + Seed.Marker());
        db.Users.Add(author);
        db.Novels.Add(novel);
        await db.SaveChangesAsync();
        return novel;
    }

    /// <summary>A chapter as stored before word counts (<paramref name="wordsCount"/> null), with these paragraphs.</summary>
    private async Task<Guid> SeedChapter(Novel novel, int? wordsCount, params ChapterParagraph[] paragraphs)
    {
        await using var db = database.CreateContext();
        var index = await db.Chapters.CountAsync(c => c.NovelId == novel.Id) + 1;
        var chapter = Seed.Chapters(novel, 1, DateTime.UtcNow, startIndex: index).Single();
        chapter.WordsCount = wordsCount;
        chapter.ParagraphsCount = paragraphs.Length;
        db.Chapters.Add(chapter);
        for (var i = 0; i < paragraphs.Length; i++)
        {
            (paragraphs[i].Id, paragraphs[i].ChapterId, paragraphs[i].OrderIndex) = (Guid.NewGuid(), chapter.Id, i);
            paragraphs[i].ContentHash = Guid.NewGuid().ToString("N");
        }
        db.ChapterParagraphs.AddRange(paragraphs);
        await db.SaveChangesAsync();
        return chapter.Id;
    }

    private static ChapterParagraph Text(string content) => new() { Content = content, ContentType = ParagraphKinds.Text };

    private static ChapterParagraph Break() => new() { Content = "* * *", ContentType = ParagraphKinds.Break };

    private static ChapterParagraph Picture(string address, string? caption = null) =>
        new() { Content = address, ContentType = ParagraphKinds.Image, Caption = caption };

    private async Task<int?> Words(Guid chapterId)
    {
        await using var db = database.CreateContext();
        return await db.Chapters.Where(c => c.Id == chapterId).Select(c => c.WordsCount).SingleAsync();
    }
}
