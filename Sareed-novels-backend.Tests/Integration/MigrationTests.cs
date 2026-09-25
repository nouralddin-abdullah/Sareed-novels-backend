using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore;

namespace Sareed_novels_backend.Tests.Integration;

/// <summary>An empty database that the test migrates itself, the way production does on startup.</summary>
public class EmptySqlServerDatabase : SqlServerDatabase
{
    public override Task InitializeAsync() => Task.CompletedTask;
}

public class MigrationTests(EmptySqlServerDatabase database) : IClassFixture<EmptySqlServerDatabase>
{
    [Fact]
    public async Task Every_migration_applies_cleanly_and_the_model_has_no_pending_changes()
    {
        await using var db = database.CreateContext();

        await db.Database.MigrateAsync();

        Assert.Empty(await db.Database.GetPendingMigrationsAsync());
        Assert.False(db.Database.HasPendingModelChanges(), "The model has changes that no migration captures.");

        // The startup backfill runs on an empty database without doing anything.
        Assert.Equal(0, await db.BackfillSearchColumnsAsync());
    }
}

public class DataFixMigrationTests(EmptySqlServerDatabase database) : IClassFixture<EmptySqlServerDatabase>
{
    private const string Bucket = "https://pub-2da766ccb48c485895ae36b58be35142.r2.dev/novel-images/";

    [Fact]
    public async Task Seeds_the_website_gift_ids_and_repairs_only_covers_that_still_hold_the_stale_url()
    {
        await using var db = database.CreateContext();
        var migrator = db.GetService<IMigrator>();
        await migrator.MigrateAsync("20260924183957_SqlSearchAndUniqueViews");

        var author = Seed.User();
        var stale = Seed.Novel(author, "امراة فى الظلام");
        stale.Id = Guid.Parse("845265f8-b34b-4dc4-9f9a-12b9deb4beab");
        stale.CoverImageUrl = Bucket + "امراة فى الظلام ";
        var changedSince = Seed.Novel(author, "الجريمة");
        changedSince.Id = Guid.Parse("8f4f8f8f-a6ba-45ce-92d1-8170b2136ba1");
        changedSince.CoverImageUrl = "https://example.test/new-cover.png";
        db.AddRange(author, stale, changedSince);
        await db.SaveChangesAsync();

        await migrator.MigrateAsync();
        db.ChangeTracker.Clear();

        var gifts = await db.Gifts.AsNoTracking().OrderBy(g => g.Cost).ToListAsync();
        Assert.Equal(["Rose", "Pizza", "Book", "Crown", "Scepter", "Castle", "Dragon", "Galaxy"], gifts.Select(g => g.Name));
        Assert.Contains(gifts, g => g.Id == Guid.Parse("ec16dfde-71b8-4e23-8ff5-d1846cdf2036") && g.Cost == 100 && g.IsActive);

        var covers = await db.Novels.AsNoTracking().ToDictionaryAsync(n => n.Id, n => n.CoverImageUrl);
        Assert.Equal(Bucket + Uri.EscapeDataString("امراة فى الظلام"), covers[stale.Id]);
        Assert.Equal("https://example.test/new-cover.png", covers[changedSince.Id]);
    }
}
