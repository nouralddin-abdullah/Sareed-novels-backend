using System.Reflection;
using Infrastructure.Migrations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Sareed_novels_backend.Tests.Integration;

/// <summary>
/// <see cref="AddProfileListVisibility"/> on accounts as production has them: every existing member shows both lists to
/// everyone (the columns' default is 'Everyone'), nothing else on the row changes; down and up again.
/// </summary>
public class ProfileListVisibilityMigrationTests(EmptySqlServerDatabase database) : IClassFixture<EmptySqlServerDatabase>
{
    private static readonly string Visibility = typeof(AddProfileListVisibility).GetCustomAttribute<MigrationAttribute>()!.Id;

    private sealed record Row(string Id, string UserName, string DisplayName, string ConcurrencyStamp);

    private sealed record Settings(string ReviewsVisibility, string CommentsVisibility);

    [Fact]
    public async Task Existing_members_show_both_lists_to_everyone_and_nothing_else_changes()
    {
        await using var db = database.CreateContext();
        var migrator = db.GetService<IMigrator>();
        var migrations = db.Database.GetMigrations().ToList();
        var before = migrations[migrations.IndexOf(Visibility) - 1];
        await migrator.MigrateAsync(before);

        // As the API inserted accounts before this migration: without the columns.
        var members = new[] { Seed.User(), Seed.User("عضو") };
        foreach (var member in members)
        {
            await Seed.InsertUserRowAsync(db, member);
        }

        await migrator.MigrateAsync(Visibility);

        Assert.Equal([new("Everyone", "Everyone"), new("Everyone", "Everyone")], await SettingsOf());
        await AssertRowsUnchanged();

        // Down drops both columns and keeps the accounts; up again, everyone sees both lists again.
        await migrator.MigrateAsync(before);
        Assert.DoesNotContain(Visibility, await db.Database.GetAppliedMigrationsAsync());
        foreach (var column in new[] { "ReviewsVisibility", "CommentsVisibility" })
        {
            Assert.Null(await db.Database
                .SqlQuery<int?>($"SELECT CAST(COL_LENGTH('AspNetUsers', {column}) AS int) AS Value")
                .SingleAsync());
        }
        await AssertRowsUnchanged();
        await migrator.MigrateAsync(Visibility);
        Assert.Equal([new("Everyone", "Everyone"), new("Everyone", "Everyone")], await SettingsOf());

        async Task<List<Settings>> SettingsOf() => await db.Database
            .SqlQuery<Settings>($"SELECT ReviewsVisibility, CommentsVisibility FROM AspNetUsers ORDER BY Id")
            .ToListAsync();

        async Task AssertRowsUnchanged()
        {
            var rows = await db.Database
                .SqlQuery<Row>($"SELECT Id, UserName, DisplayName, ConcurrencyStamp FROM AspNetUsers ORDER BY Id")
                .ToListAsync();
            Assert.Equal(
                members.Select(m => new Row(m.Id, m.UserName!, m.DisplayName, m.ConcurrencyStamp!)).OrderBy(r => r.Id, StringComparer.Ordinal),
                rows.OrderBy(r => r.Id, StringComparer.Ordinal));
        }
    }
}
