using System.Reflection;
using Application.Wallet;
using Infrastructure.Migrations;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Sareed_novels_backend.Tests.Integration;

/// <summary>
/// <see cref="ArabicGiftNamesAndTransactionDetails"/> on a database with the gift catalog production has and English
/// wallet entries in every shape the code ever wrote, plus the ones it must leave alone.
/// </summary>
public class ArabicWalletMigrationTests(EmptySqlServerDatabase database) : IClassFixture<EmptySqlServerDatabase>
{
    private const string Before = "20260927222840_AddReportsAndBlocks";
    private static readonly string Fix = typeof(ArabicGiftNamesAndTransactionDetails).GetCustomAttribute<MigrationAttribute>()!.Id;

    private sealed record GiftRow(string Name, string NameAr);

    private static Task<List<GiftRow>> Gifts(ApplicationDbContext db) =>
        db.Database.SqlQuery<GiftRow>($"SELECT Name, NameAr FROM Gifts").ToListAsync();

    private static async Task<Guid> Entry(ApplicationDbContext db, string userId, string type, string description)
    {
        var id = Guid.NewGuid();
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO PointTransactions (Id, UserId, Type, Amount, BalanceBefore, BalanceAfter, Description, CreatedAt)
            VALUES ({id}, {userId}, {type}, 100, 0, 100, {description}, {DateTime.UtcNow})
            """);
        return id;
    }

    private static async Task<string> Description(ApplicationDbContext db, Guid id) =>
        await db.Database.SqlQuery<string>($"SELECT Description AS Value FROM PointTransactions WHERE Id = {id}").SingleAsync();

    [Fact]
    public async Task Gifts_get_arabic_names_and_english_wallet_entries_are_rewritten_where_certain()
    {
        await using var db = database.CreateContext();
        var migrator = db.GetService<IMigrator>();
        await migrator.MigrateAsync(Before); // the catalog of eight gifts is seeded by then, as in production

        var user = Seed.User("قارئ");
        await Seed.InsertUserRowAsync(db, user);
        await db.Database.ExecuteSqlRawAsync("""
            INSERT INTO Gifts (Id, Name, ImageUrl, Cost, IsActive, CreatedAt)
            VALUES (NEWID(), N'Unicorn', N'https://www.sardnovels.com/gifts/unicorn.png', 50, 0, '2026-09-26')
            """);

        var expected = new Dictionary<Guid, string>
        {
            [await Entry(db, user.Id, "RechargeApproved", "Recharge approved: 500 points (55.00 EGP via VodafoneCash)")] =
                "شحن رصيد: 500 نقطة (55.00 جنيه عبر فودافون كاش)",
            // The wallet's first week wrote the type as "Recharge".
            [await Entry(db, user.Id, "Recharge", "Recharge approved: 1000 points (110.00 EGP via InstaPay)")] =
                "شحن رصيد: 1000 نقطة (110.00 جنيه عبر إنستاباي)",
            [await Entry(db, user.Id, "WithdrawalApproved", "Withdrawal approved: 1000 points (90.00 EGP via PayPal)")] =
                "سحب رصيد: 1000 نقطة (90.00 جنيه عبر باي بال)",
            [await Entry(db, user.Id, "GiftSent", "Sent 3x Rose to ظل الأمير")] = "أرسلت وردة ×3 إلى رواية «ظل الأمير»",
            [await Entry(db, user.Id, "GiftSent", "Sent 12x Dragon to Road to Cairo")] = "أرسلت تنين ×12 إلى رواية «Road to Cairo»",
            [await Entry(db, user.Id, "GiftReceived", "Received 2x Castle from نور on ظل الأمير")] =
                "استلمت قلعة ×2 من نور على رواية «ظل الأمير»",
            [await Entry(db, user.Id, "GiftReceived", "Received 1x Galaxy from sarduser123456 on Two Moons")] =
                "استلمت مجرة ×1 من sarduser123456 على رواية «Two Moons»",
            [await Entry(db, user.Id, "PrivilegeSubscription", "Subscribed to privilege for novel: ظل الأمير")] =
                "اشتراك دائم في امتيازات رواية «ظل الأمير»",
            [await Entry(db, user.Id, "PrivilegeRevenue", "Privilege subscription revenue from novel: Crown: the novel")] =
                "عائد اشتراك في امتيازات رواية «Crown: the novel»",
        };

        // Left as they are: where the split would be a guess, a gift that isn't in the catalog, malformed text, other
        // types, and entries that are Arabic already.
        var kept = new Dictionary<Guid, string>();
        foreach (var (type, description) in new[]
        {
            ("GiftReceived", "Received 1x Book from Moon on Sea on Night"), // " on " twice: which is the sender?
            ("GiftSent", "Sent 2x Phoenix to ظل الأمير"),
            ("GiftSent", "Sent x to"),
            ("GiftSent", "Sent gifts"),
            ("GiftReceived", "Received many gifts"),
            ("GiftReceived", "Sent 1x Rose to X"), // a gift description under another type
            ("RechargeApproved", "Recharge approved: manual top-up"),
            ("PlayPurchase", "شراء 500 نقطة عبر Google Play"),
        })
        {
            kept[await Entry(db, user.Id, type, description)] = description;
        }
        // A gift of this database's catalog outside the eight: rewritten with the name it falls back to.
        var unicorn = await Entry(db, user.Id, "GiftSent", "Sent 2x Unicorn to ظل الأمير");

        await migrator.MigrateAsync(Fix);

        var gifts = (await Gifts(db)).ToDictionary(g => g.Name, g => g.NameAr);
        Assert.Equal("وردة", gifts["Rose"]);
        Assert.Equal("بيتزا", gifts["Pizza"]);
        Assert.Equal("كتاب", gifts["Book"]);
        Assert.Equal("تاج", gifts["Crown"]);
        Assert.Equal("صولجان", gifts["Scepter"]);
        Assert.Equal("قلعة", gifts["Castle"]);
        Assert.Equal("تنين", gifts["Dragon"]);
        Assert.Equal("مجرة", gifts["Galaxy"]);
        Assert.Equal("Unicorn", gifts["Unicorn"]); // outside the list: its English name until an admin sets one

        foreach (var (id, description) in expected.Concat(kept))
        {
            Assert.Equal(description, await Description(db, id));
        }
        Assert.Equal("أرسلت Unicorn ×2 إلى رواية «ظل الأمير»", await Description(db, unicorn));

        // The rewritten entries read exactly like new ones.
        Assert.Equal(expected.Values.ElementAt(0), TransactionDescriptions.RechargeApproved(500, 55.00m, "VodafoneCash"));
        Assert.Equal(expected.Values.ElementAt(2), TransactionDescriptions.WithdrawalApproved(1000, 90m, "PayPal"));
        Assert.Equal(expected.Values.ElementAt(3), TransactionDescriptions.GiftSent("وردة", 3, "ظل الأمير"));
        Assert.Equal(expected.Values.ElementAt(5), TransactionDescriptions.GiftReceived("قلعة", 2, "نور", "ظل الأمير"));
        Assert.Equal(expected.Values.ElementAt(7), TransactionDescriptions.PrivilegeSubscription("ظل الأمير"));
        Assert.Equal(expected.Values.ElementAt(8), TransactionDescriptions.PrivilegeRevenue("Crown: the novel"));

        // Down and Up again: the columns come back, the names are seeded again and nothing is rewritten twice.
        var afterFirstRun = new Dictionary<Guid, string>();
        foreach (var id in expected.Keys.Concat(kept.Keys).Append(unicorn))
        {
            afterFirstRun[id] = await Description(db, id);
        }
        await migrator.MigrateAsync(Before);
        await migrator.MigrateAsync(Fix);
        foreach (var (id, description) in afterFirstRun)
        {
            Assert.Equal(description, await Description(db, id));
        }
        Assert.Equal("وردة", (await Gifts(db)).Single(g => g.Name == "Rose").NameAr);

        await migrator.MigrateAsync();
        Assert.Empty(await db.Database.GetPendingMigrationsAsync());
    }
}
