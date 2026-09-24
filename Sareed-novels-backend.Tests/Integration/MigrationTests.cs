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
