using System.Reflection;
using Domain.Entities;
using Infrastructure.Migrations;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Sareed_novels_backend.Tests.Integration;

/// <summary>
/// The email-shaped user name fix in <see cref="ReplaceEmailUserNamesAndRevokeTokens"/>, on a database seeded the way
/// production looks (made-up names in the shapes found there on 2026-09-27: gmail addresses, addresses cut off at 20
/// characters by the old sign-up form, "@" as a separator) plus the edge cases: collisions with existing names and
/// within the batch, too little left for a handle, letters outside a-z, punctuation at the ends, long local parts.
/// </summary>
public class EmailUserNameMigrationTests(EmptySqlServerDatabase database) : IClassFixture<EmptySqlServerDatabase>
{
    private const string Before = "20260925105656_RepairNovelCoversAndSeedGifts";
    private static readonly string Fix = typeof(ReplaceEmailUserNamesAndRevokeTokens).GetCustomAttribute<MigrationAttribute>()!.Id;

    private sealed record UserRow(string Id, string UserName, string NormalizedUserName, string SearchName);
    private sealed record ChangeRow(string UserId, string OldUserName, string OldNormalizedUserName);

    private static User Member(string userName, int day, string? displayName = null)
    {
        var user = Seed.User(displayName ?? "عضو", userName);
        user.NormalizedUserName = userName.ToUpperInvariant();
        user.CreatedAt = new DateTime(2025, 1, day, 0, 0, 0, DateTimeKind.Utc);
        user.SearchName = "";
        return user;
    }

    private static async Task<Dictionary<string, UserRow>> Users(ApplicationDbContext db) =>
        (await db.Database.SqlQuery<UserRow>($"SELECT Id, UserName, NormalizedUserName, SearchName FROM AspNetUsers").ToListAsync())
        .ToDictionary(u => u.Id);

    private static Task<List<ChangeRow>> Changes(ApplicationDbContext db) =>
        db.Database.SqlQuery<ChangeRow>($"SELECT UserId, OldUserName, OldNormalizedUserName FROM UserNameChanges").ToListAsync();

    private static Task<List<string>> ActionUrls(ApplicationDbContext db) =>
        db.Database.SqlQuery<string>($"SELECT ActionUrl AS Value FROM Notifications").ToListAsync();

    private static Task<List<string>> Descriptions(ApplicationDbContext db) =>
        db.Database.SqlQuery<string>($"SELECT Description AS Value FROM PointTransactions").ToListAsync();

    private static Task Notify(ApplicationDbContext db, User recipient, string actionUrl) => db.Database.ExecuteSqlInterpolatedAsync($"""
        INSERT INTO Notifications (Id, UserId, Type, ActorId, ActorDisplayName, Message, ActionUrl, IsRead, CreatedAt)
        VALUES ({Guid.NewGuid()}, {recipient.Id}, N'NewFollower', {recipient.Id}, N'عضو', N'بدأ بمتابعتك', {actionUrl}, 0, {DateTime.UtcNow})
        """);

    private static Task Ledger(ApplicationDbContext db, User owner, string type, string description) => db.Database.ExecuteSqlInterpolatedAsync($"""
        INSERT INTO PointTransactions (Id, UserId, Type, Amount, BalanceBefore, BalanceAfter, Description, CreatedAt)
        VALUES ({Guid.NewGuid()}, {owner.Id}, {type}, 100, 0, 100, {description}, {DateTime.UtcNow})
        """);

    [Fact]
    public async Task Email_shaped_user_names_become_unique_handles_and_old_links_keep_working()
    {
        await using var db = database.CreateContext();
        var migrator = db.GetService<IMigrator>();
        await migrator.MigrateAsync(Before);

        var existing = Member("noor.reader", 1);
        var existing20 = Member("abcdefghijklmnopqrst", 1);
        var gmail = Member("Noor.Reader@gmail.com", 2, "نور");
        var sameLocalPart = Member("noor.reader@example.test", 3);
        var digits = Member("youssefq110@gmail.com", 4, "يوسف");
        var cutOff = Member("abcdefghijk12@gmail.", 5); // the old sign-up form cut user names at 20 characters
        var cutOffWhole = Member("abcdefghijk12@gmail.com", 6);
        var tooShort = Member("ab@gmail.com", 7);
        var noLocalPart = Member("@sard", 8);
        var arabicLocalPart = Member("ليلى@example.test", 9);
        var mixed = Member("ليلى.Salem+sard@example.test", 10);
        var dots = Member("..dots__@example.test", 11);
        var longLocalPart = Member("a.very.long.local.part.here@example.test", 12);
        var twenty = Member("abcdefghijklmnopqrst@example.test", 13);
        var upper = Member("MOHAMED.ALI99@Example.Test", 14);
        var untouched = Member("plain_member", 15);
        // Old Google sign-ups without a name got their address as display name too.
        var addressAsDisplayName = Member("salma.k@example.test", 16, "salma.k@example.test");
        var cutInDomain = Member("sara.mohamed123@ho", 17);
        var separator = Member("ali@2024", 18); // "@" as a separator, not an address
        var all = new[] { existing, existing20, gmail, sameLocalPart, digits, cutOff, cutOffWhole, tooShort, noLocalPart, arabicLocalPart, mixed, dots, longLocalPart, twenty, upper, untouched, addressAsDisplayName, cutInDomain, separator };
        foreach (var user in all)
        {
            await Seed.InsertUserRowAsync(db, user);
        }

        await Notify(db, existing, "/profile/Noor.Reader@gmail.com");
        await Notify(db, existing, "/profile/plain_member");
        await Notify(db, existing, "/novel/x1234-رواية/chapter/5");
        await Ledger(db, existing, "GiftReceived", "Received 2x Rose from Noor.Reader@gmail.com on رواية on the sea");
        await Ledger(db, existing, "GiftReceived", "Received 1x Crown from youssefq110@gmail.com on رواية");
        await Ledger(db, existing, "GiftReceived", "Received 3x Book from salma.k@example.test on رواية");
        await Ledger(db, existing, "GiftReceived", "Received 1x Rose from plain_member on رواية");
        await Ledger(db, gmail, "GiftSent", "Sent 2x Rose to رواية on the sea");

        await migrator.MigrateAsync(Fix);

        var users = await Users(db);
        Assert.DoesNotContain(users.Values, u => u.UserName.Contains('@'));
        Assert.Equal("noor.reader", users[existing.Id].UserName);
        Assert.Equal("noor.reader2", users[gmail.Id].UserName);
        Assert.Equal("noor.reader3", users[sameLocalPart.Id].UserName);
        Assert.Equal("youssefq110", users[digits.Id].UserName);
        Assert.Equal("abcdefghijk12", users[cutOff.Id].UserName);
        Assert.Equal("abcdefghijk122", users[cutOffWhole.Id].UserName);
        Assert.Matches("^sarduser[0-9]{6}$", users[tooShort.Id].UserName);
        Assert.Matches("^sarduser[0-9]{6}$", users[noLocalPart.Id].UserName);
        Assert.Matches("^sarduser[0-9]{6}$", users[arabicLocalPart.Id].UserName);
        Assert.Equal("salemsard", users[mixed.Id].UserName);
        Assert.Equal("dots", users[dots.Id].UserName);
        Assert.Equal("a.very.long.local.pa", users[longLocalPart.Id].UserName);
        Assert.Equal("abcdefghijklmnopqrs2", users[twenty.Id].UserName);
        Assert.Equal("mohamed.ali99", users[upper.Id].UserName);
        Assert.Equal("plain_member", users[untouched.Id].UserName);
        Assert.Equal("salma.k", users[addressAsDisplayName.Id].UserName);
        Assert.Equal("sara.mohamed123", users[cutInDomain.Id].UserName);
        Assert.Equal("ali", users[separator.Id].UserName);

        // Valid handles: 3-20 of a-z 0-9 . _ -, no punctuation at the ends, unique as Identity compares them.
        var renamed = all.Where(u => u.UserName!.Contains('@')).ToList();
        Assert.All(renamed, u => Assert.Matches("^[a-z0-9][a-z0-9._-]{1,18}[a-z0-9]$", users[u.Id].UserName));
        Assert.All(users.Values, u => Assert.Equal(u.UserName.ToUpperInvariant(), u.NormalizedUserName));
        Assert.Equal(users.Count, users.Values.Select(u => u.NormalizedUserName).Distinct().Count());

        // Renamed members' search text is refilled at startup; the others' is left alone.
        Assert.All(renamed, u => Assert.Equal("", users[u.Id].SearchName));

        // Every old name is kept for old links, as Identity normalizes it.
        var changes = await Changes(db);
        Assert.Equal(
            renamed.Select(u => $"{u.Id} {u.UserName} {u.NormalizedUserName}").Order(StringComparer.Ordinal),
            changes.Select(c => $"{c.UserId} {c.OldUserName} {c.OldNormalizedUserName}").Order(StringComparer.Ordinal));

        Assert.Equal(
            ["/novel/x1234-رواية/chapter/5", "/profile/noor.reader2", "/profile/plain_member"],
            (await ActionUrls(db)).Order(StringComparer.Ordinal));
        Assert.Equal(
            [
                "Received 1x Crown from يوسف on رواية",
                "Received 1x Rose from plain_member on رواية",
                "Received 2x Rose from نور on رواية on the sea",
                // The display name is the address itself: the new handle instead.
                "Received 3x Book from salma.k on رواية",
                "Sent 2x Rose to رواية on the sea",
            ],
            (await Descriptions(db)).Order(StringComparer.Ordinal));

        // Idempotent: a second run finds nothing to do.
        var usersBefore = await Users(db);
        await db.Database.ExecuteSqlRawAsync(ReplaceEmailUserNamesAndRevokeTokens.ReplaceEmailUserNamesSql);
        Assert.Equal(usersBefore, await Users(db));
        Assert.Equal(changes.Count, (await Changes(db)).Count);

        // Down puts the old names back; Up again gives the same handles (deterministic).
        await migrator.MigrateAsync(Before);
        var restored = await Users(db);
        Assert.All(all, u => Assert.Equal(u.UserName, restored[u.Id].UserName));
        Assert.Contains("/profile/Noor.Reader@gmail.com", await ActionUrls(db));
        await migrator.MigrateAsync(Fix);
        Assert.Equal(
            usersBefore.Values.Select(u => $"{u.Id} {u.UserName}").Order(StringComparer.Ordinal),
            (await Users(db)).Values.Select(u => $"{u.Id} {u.UserName}").Order(StringComparer.Ordinal));

        // On the current model, the startup backfill gives the renamed members search text from their display name
        // and new handle: their address is gone from it (unless it is their display name).
        await migrator.MigrateAsync();
        await db.BackfillSearchColumnsAsync();
        var searchNames = await Users(db);
        Assert.Equal("يوسف youssefq110", searchNames[digits.Id].SearchName);
        Assert.All(renamed.Where(u => !u.DisplayName.Contains('@')), u => Assert.DoesNotContain("gmail", searchNames[u.Id].SearchName));
        Assert.All(renamed.Where(u => !u.DisplayName.Contains('@')), u => Assert.DoesNotContain("example", searchNames[u.Id].SearchName));
    }
}
