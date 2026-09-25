using Application.Covers;
using Application.Covers.Commands.ConvertLegacyCovers;
using Application.Covers.Queries.GetCoverStatus;
using Application.Services;
using Domain.Entities;
using Infrastructure.Repositories;
using Infrastructure.Services;
using Infrastructure.Services.Covers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using SkiaSharp;

namespace Sareed_novels_backend.Tests.Integration;

/// <summary>The admin backfill that converts legacy covers, on a real database with an in-memory bucket.</summary>
public class CoverConversionTests : SqlServerDatabase
{
    private const string Bucket = "https://pub-test.r2.dev";

    private readonly InMemoryObjectStorage storage = new(Bucket);
    private User author = default!;

    public override async Task InitializeAsync()
    {
        await base.InitializeAsync();
        await using var db = CreateContext();
        author = Seed.User();
        db.Users.Add(author);
        await db.SaveChangesAsync();
    }

    private NovelCoverService CoverService() =>
        new(storage, Substitute.For<IFileUploadService>(), NullLogger<NovelCoverService>.Instance);

    private async Task<ConvertLegacyCoversResult> Run(int batchSize = 10, Guid? after = null, bool dryRun = false, INovelCoverService? covers = null)
    {
        await using var db = CreateContext();
        var handler = new ConvertLegacyCoversCommandHandler(new NovelsRepository(db), covers ?? CoverService(),
            NullLogger<ConvertLegacyCoversCommandHandler>.Instance);
        return await handler.Handle(new ConvertLegacyCoversCommand { BatchSize = batchSize, After = after, DryRun = dryRun }, CancellationToken.None);
    }

    /// <summary>Runs batch after batch, following the cursor, until the end of the list.</summary>
    private async Task<List<ConvertLegacyCoversResult>> RunToEnd(int batchSize)
    {
        var results = new List<ConvertLegacyCoversResult>();
        Guid? cursor = null;
        do
        {
            var result = await Run(batchSize, cursor);
            results.Add(result);
            cursor = result.NextCursor;
        } while (cursor is not null);
        return results;
    }

    private async Task<Novel> AddNovel(string coverUrl, bool deleted = false)
    {
        await using var db = CreateContext();
        var novel = Seed.Novel(author, "رواية " + Seed.Marker());
        novel.CoverImageUrl = coverUrl;
        novel.IsDeleted = deleted;
        db.Novels.Add(novel);
        await db.SaveChangesAsync();
        return novel;
    }

    private async Task<Dictionary<Guid, string>> Covers()
    {
        await using var db = CreateContext();
        return await db.Novels.IgnoreQueryFilters().AsNoTracking().ToDictionaryAsync(n => n.Id, n => n.CoverImageUrl);
    }

    private static byte[] Png(int width, int height) =>
        TestImages.Halves(width, height, SKColors.Crimson, SKColors.Navy, SKEncodedImageFormat.Png, vertical: true);

    [Fact]
    public async Task Legacy_covers_are_converted_once_and_a_second_run_changes_nothing()
    {
        // As in production: raw Arabic keys, a key ending in a space, a percent-encoded URL, a Wattpad-sized JPEG.
        var spaced = await AddNovel(storage.AddLegacy("novel-images/الرسالة الأخيرة ", Png(1254, 1254)));
        var encoded = await AddNovel($"{Bucket}/novel-images/{Uri.EscapeDataString("همس الأوتار")}");
        storage.Objects["novel-images/همس الأوتار"] = (Png(843, 1264), "image/png");
        var wattpad = await AddNovel(storage.AddLegacy("novel-images/بنت الشيطان",
            TestImages.Halves(512, 800, SKColors.Crimson, SKColors.Navy, SKEncodedImageFormat.Jpeg, vertical: true), "image/jpeg"));
        var alreadyStandard = await AddNovel($"{Bucket}/{NovelCovers.KeyPrefix(Guid.NewGuid(), Guid.NewGuid())}/960.webp");
        var deleted = await AddNovel(storage.AddLegacy("novel-images/محذوفة", Png(600, 900)), deleted: true);
        var before = await Covers();

        var first = await RunToEnd(batchSize: 2);

        Assert.Equal(3, first.Sum(r => r.Converted));
        Assert.Equal(0, first.Sum(r => r.Failed));
        Assert.Equal(0, first[^1].Remaining);
        var after = await Covers();
        foreach (var novel in new[] { spaced, encoded, wattpad })
        {
            Assert.True(NovelCovers.IsStandard(after[novel.Id]), after[novel.Id]);
            Assert.StartsWith($"{Bucket}/novel-covers/{novel.Id}/", after[novel.Id]);
            Assert.NotNull(storage.Objects[storage.KeyOf(NovelCovers.JpegUrlOf(after[novel.Id])!)!].Bytes);
        }
        Assert.EndsWith("/512.webp", after[wattpad.Id]);
        Assert.Equal(before[alreadyStandard.Id], after[alreadyStandard.Id]);
        Assert.Equal(before[deleted.Id], after[deleted.Id]);
        // The originals stay in the bucket (other rows, notifications and caches may still point at them).
        Assert.True(storage.Objects.ContainsKey("novel-images/الرسالة الأخيرة "));

        var puts = storage.Puts;
        var second = await Run();

        Assert.Empty(second.Items);
        Assert.Null(second.NextCursor);
        Assert.Equal(0, second.Remaining);
        Assert.Equal(puts, storage.Puts);
        Assert.Equal(after, await Covers());
    }

    [Fact]
    public async Task A_dry_run_reads_and_reports_but_writes_nothing()
    {
        var landscape = await AddNovel(storage.AddLegacy("novel-images/عرضية", Png(1400, 1100)));
        var puts = storage.Puts;

        var result = await Run(dryRun: true);

        var item = Assert.Single(result.Items, i => i.NovelId == landscape.Id);
        Assert.Equal("wouldConvert", item.Outcome);
        Assert.Equal("fit", item.Layout);
        Assert.Equal((1400, 1100), (item.SourceWidth, item.SourceHeight));
        Assert.Null(item.NewUrl);
        Assert.Equal(puts, storage.Puts);
        Assert.Equal(landscape.CoverImageUrl, (await Covers())[landscape.Id]);
        Assert.Equal(1, result.Remaining);
    }

    [Fact]
    public async Task Covers_that_fail_are_reported_the_cursor_moves_past_them_and_a_fresh_run_retries_them()
    {
        var missing = await AddNovel($"{Bucket}/novel-images/غير موجود");
        var notAnImage = await AddNovel(storage.AddLegacy("novel-images/ليست صورة", "<html></html>"u8.ToArray()));
        var foreign = await AddNovel("https://example.test/cover.png");
        var good = await AddNovel(storage.AddLegacy("novel-images/جيدة", Png(700, 1050)));

        var results = await RunToEnd(batchSize: 1);

        Assert.Equal(1, results.Sum(r => r.Converted));
        var failures = results.SelectMany(r => r.Items).Where(i => i.Outcome == "failed").ToDictionary(i => i.NovelId);
        Assert.Equal(3, failures.Count);
        Assert.Equal(CoverErrorCodes.Unreadable, failures[missing.Id].ErrorCode);
        Assert.Equal(CoverErrorCodes.UnsupportedFormat, failures[notAnImage.Id].ErrorCode);
        Assert.Contains("configured bucket", failures[foreign.Id].Error);
        Assert.Equal(3, results[^1].Remaining);
        Assert.True(NovelCovers.IsStandard((await Covers())[good.Id]));

        // Once the missing file is restored, a run from the start picks it up; the others fail again, nothing else changes.
        storage.Objects["novel-images/غير موجود"] = (Png(600, 900), "image/png");
        var retry = await Run();
        Assert.Equal(1, retry.Converted);
        Assert.Equal(2, retry.Failed);
        Assert.Equal(2, retry.Remaining);
    }

    [Fact]
    public async Task A_cover_changed_by_its_author_during_the_run_is_left_alone_and_the_unused_files_are_removed()
    {
        var novel = await AddNovel(storage.AddLegacy("novel-images/سباق", Png(600, 900)));
        var authorsNewCover = $"{Bucket}/{NovelCovers.KeyPrefix(novel.Id, Guid.NewGuid())}/960.webp";
        var real = CoverService();
        var racing = Substitute.For<INovelCoverService>();
        racing.ProcessorAvailable.Returns(true);
        racing.ConvertExistingAsync(novel.Id, novel.CoverImageUrl, false, Arg.Any<CancellationToken>()).Returns(async call =>
        {
            var conversion = await real.ConvertExistingAsync(novel.Id, novel.CoverImageUrl, false);
            await using var db = CreateContext();
            await new NovelsRepository(db).SetCoverUrlAsync(novel.Id, authorsNewCover);
            return conversion;
        });
        racing.DeleteStandardCoverAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(call => real.DeleteStandardCoverAsync(call.Arg<string>()));

        var result = await Run(covers: racing);

        var item = Assert.Single(result.Items);
        Assert.Equal("skipped", item.Outcome);
        Assert.Equal(authorsNewCover, (await Covers())[novel.Id]);
        Assert.DoesNotContain(storage.Objects.Keys, k => k.StartsWith($"novel-covers/{novel.Id}/") && !authorsNewCover.Contains(k));
    }

    [Fact]
    public async Task Status_counts_standard_and_legacy_covers_of_novels_that_are_not_deleted()
    {
        await AddNovel(storage.AddLegacy("novel-images/قديمة", Png(600, 900)));
        await AddNovel($"{Bucket}/{NovelCovers.KeyPrefix(Guid.NewGuid(), Guid.NewGuid())}/640.webp");
        await AddNovel(storage.AddLegacy("novel-images/محذوفة2", Png(600, 900)), deleted: true);

        await using var db = CreateContext();
        var status = await new GetCoverStatusQueryHandler(new NovelsRepository(db), CoverService())
            .Handle(new GetCoverStatusQuery(), CancellationToken.None);

        Assert.Equal(new CoverStatus(true, 2, 1, 1), status);
    }

    [Fact]
    public async Task Without_an_image_processor_nothing_is_attempted()
    {
        await AddNovel(storage.AddLegacy("novel-images/بلا معالج", Png(600, 900)));
        var unavailable = Substitute.For<INovelCoverService>();
        unavailable.ProcessorAvailable.Returns(false);

        var result = await Run(covers: unavailable);

        Assert.False(result.ProcessorAvailable);
        Assert.Empty(result.Items);
        Assert.Equal(1, result.Remaining);
        await unavailable.DidNotReceiveWithAnyArgs().ConvertExistingAsync(default, default!, default, default);
    }
}
