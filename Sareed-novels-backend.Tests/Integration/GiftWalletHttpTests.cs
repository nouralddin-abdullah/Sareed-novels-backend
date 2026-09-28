using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Domain.Constants;
using Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Sareed_novels_backend.Tests.Integration;

/// <summary>
/// Gifts in Arabic (the catalog's nameAr, the author's notification), wallet entries that say what they were about
/// (type, novel, gift, count) in Arabic, and the gift history with its novel.
/// </summary>
[Collection(ReaderApiCollection.Name)]
public class GiftWalletHttpTests(SardApiFactory api)
{
    // The catalog production has (seeded by migrations, as on the live database).
    private static readonly Guid Rose = Guid.Parse("ec16dfde-71b8-4e23-8ff5-d1846cdf2036");

    private static readonly HashSet<Guid> CatalogIds =
    [
        Rose, Guid.Parse("88103b01-2e5b-4d06-9ff3-2724f4afba52"), Guid.Parse("9e17512a-269a-43e8-a571-1a1dc541cb5a"),
        Guid.Parse("48bdfb35-9f2c-4198-80c1-58f28eb648ef"), Guid.Parse("e6bfb3e7-6273-4e6b-a577-6afa71055bce"),
        Guid.Parse("a4005ee7-f2a5-488a-8757-574030513cd4"), Guid.Parse("955f63a6-5f4e-4b10-8743-8ea11f544bae"),
        Guid.Parse("50ca3576-e6ee-4708-8d7d-4e9ce82cf722"),
    ];

    private async Task Fund(ApiUser user, decimal balance)
    {
        await using var db = api.Db();
        db.UserWallets.Add(new UserWallet { Id = Guid.NewGuid(), UserId = user.Id, CurrentBalance = balance });
        await db.SaveChangesAsync();
    }

    private Task<HttpResponseMessage> SendGift(ApiUser sender, Guid novelId, int count, Guid? giftId = null) =>
        api.Send(HttpMethod.Post, "/api/gift/send", sender, JsonContent.Create(new { giftId = giftId ?? Rose, novelId, count }));

    private async Task<List<JsonElement>> Transactions(ApiUser user) =>
        (await (await api.Get("/api/wallet/transactions", user)).OkJson()).GetProperty("transactions").EnumerateArray().ToList();

    [Fact]
    public async Task The_catalog_has_arabic_names()
    {
        var catalog = await (await api.Get("/api/gift?pageSize=100")).OkJson();

        // The eight gifts of production's catalog, by the ids the migrations seeded (other tests add gifts of their own).
        var names = catalog.GetProperty("items").EnumerateArray()
            .Where(g => CatalogIds.Contains(g.GetProperty("id").GetGuid()))
            .ToDictionary(g => g.GetProperty("name").GetString()!, g => g.GetProperty("nameAr").GetString());
        Assert.Equal(8, names.Count);
        Assert.Equal("وردة", names["Rose"]);
        Assert.Equal("بيتزا", names["Pizza"]);
        Assert.Equal("كتاب", names["Book"]);
        Assert.Equal("تاج", names["Crown"]);
        Assert.Equal("صولجان", names["Scepter"]);
        Assert.Equal("قلعة", names["Castle"]);
        Assert.Equal("تنين", names["Dragon"]);
        Assert.Equal("مجرة", names["Galaxy"]);
    }

    [Fact]
    public async Task A_gift_is_arabic_in_both_wallets_and_the_notification_and_says_what_it_was_about()
    {
        var reader = await api.SignUp();
        var author = await api.SignUp();
        var novel = await api.AddNovel(author, title: "ظل الأمير " + Seed.Marker());
        await Fund(reader, 1000);

        var sent = await (await SendGift(reader, novel.Id, 3)).OkJson();
        Assert.True(sent.GetProperty("success").GetBoolean());

        var spent = Assert.Single(await Transactions(reader));
        Assert.Equal(TransactionType.GiftSent, spent.GetProperty("type").GetString());
        Assert.Equal($"أرسلت وردة ×3 إلى رواية «{novel.Title}»", spent.GetProperty("description").GetString());
        Assert.Equal(-300m, spent.GetProperty("amount").GetDecimal());
        AssertAbout(spent, novel, Rose, 3);

        var earned = Assert.Single(await Transactions(author));
        Assert.Equal(TransactionType.GiftReceived, earned.GetProperty("type").GetString());
        Assert.Equal($"استلمت وردة ×3 من {reader.UserName} على رواية «{novel.Title}»", earned.GetProperty("description").GetString());
        AssertAbout(earned, novel, Rose, 3);

        // The author's notification names the gift in Arabic (it used to embed "Rose").
        var notification = Assert.Single(await api.WaitForNotificationsFrom(author, reader));
        Assert.Equal($"{reader.UserName} أرسل لك 3x وردة على رواية '{novel.Title}'", notification.Message);
    }

    private static void AssertAbout(JsonElement entry, Novel novel, Guid? giftId, int? count)
    {
        Assert.Equal(novel.Id, entry.GetProperty("novelId").GetGuid());
        Assert.Equal(novel.Slug, entry.GetProperty("novelSlug").GetString());
        Assert.Equal(novel.Title, entry.GetProperty("novelTitle").GetString());
        if (giftId is null)
        {
            Assert.Equal(JsonValueKind.Null, entry.GetProperty("giftId").ValueKind);
            Assert.Equal(JsonValueKind.Null, entry.GetProperty("giftCount").ValueKind);
        }
        else
        {
            Assert.Equal(giftId, entry.GetProperty("giftId").GetGuid());
            Assert.Equal(count, entry.GetProperty("giftCount").GetInt32());
        }
    }

    [Fact]
    public async Task A_privilege_subscription_names_its_novel_in_both_wallets()
    {
        var reader = await api.SignUp();
        var author = await api.SignUp();
        var novel = await api.AddNovel(author);
        await Fund(reader, 500);
        await using (var db = api.Db())
        {
            db.NovelPrivileges.Add(new NovelPrivilege
            {
                Id = Guid.NewGuid(), NovelId = novel.Id, IsEnabled = true, SubscriptionCost = 200, CurrentLockedCount = 5, PrivilegeStartSequence = 11
            });
            await db.SaveChangesAsync();
        }

        (await api.Send(HttpMethod.Post, $"/api/novel/{novel.Id}/privilege/subscribe", reader)).EnsureSuccessStatusCode();

        var paid = Assert.Single(await Transactions(reader));
        Assert.Equal(TransactionType.PrivilegeSubscription, paid.GetProperty("type").GetString());
        Assert.Equal($"اشتراك دائم في امتيازات رواية «{novel.Title}»", paid.GetProperty("description").GetString());
        AssertAbout(paid, novel, null, null);
        var revenue = Assert.Single(await Transactions(author));
        Assert.Equal(TransactionType.PrivilegeRevenue, revenue.GetProperty("type").GetString());
        Assert.Equal($"عائد اشتراك في امتيازات رواية «{novel.Title}»", revenue.GetProperty("description").GetString());
        AssertAbout(revenue, novel, null, null);
    }

    [Fact]
    public async Task Approved_recharges_and_withdrawals_are_described_in_arabic()
    {
        var user = await api.SignUp();
        var admin = await api.SignUpAdmin();
        var proof = new ByteArrayContent([0x89, 0x50, 0x4E, 0x47]);
        proof.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        var form = ReaderApi.Form(("PointsRequested", "500"), ("PaymentMethod", PaymentMethod.VodafoneCash));
        form.Add(proof, "PaymentProof", "proof.png");
        (await api.Send(HttpMethod.Post, "/api/wallet/recharge", user, form)).EnsureSuccessStatusCode();
        Guid rechargeId;
        await using (var db = api.Db())
        {
            rechargeId = await db.RechargeRequests.Where(r => r.UserId == user.Id).Select(r => r.Id).SingleAsync();
        }
        (await api.Send(HttpMethod.Patch, $"/api/admin/recharge/{rechargeId}/approve", admin)).EnsureSuccessStatusCode();

        var recharge = Assert.Single(await Transactions(user));
        Assert.Equal(TransactionType.RechargeApproved, recharge.GetProperty("type").GetString());
        Assert.Equal("شحن رصيد: 500 نقطة (55.00 جنيه عبر فودافون كاش)", recharge.GetProperty("description").GetString());
        Assert.Equal(JsonValueKind.Null, recharge.GetProperty("novelId").ValueKind);

        var author = await api.SignUp();
        await Fund(author, 1000);
        (await api.Send(HttpMethod.Post, "/api/wallet/withdraw", author, JsonContent.Create(new
        {
            pointsRequested = 1000, withdrawalMethod = PaymentMethod.InstaPay, paymentDetails = "01000000000"
        }))).EnsureSuccessStatusCode();
        Guid withdrawalId;
        await using (var db = api.Db())
        {
            withdrawalId = await db.WithdrawalRequests.Where(r => r.UserId == author.Id).Select(r => r.Id).SingleAsync();
        }
        (await api.Send(HttpMethod.Patch, $"/api/admin/withdraw/{withdrawalId}/approve", admin)).EnsureSuccessStatusCode();

        var withdrawal = Assert.Single(await Transactions(author));
        Assert.Equal(TransactionType.WithdrawalApproved, withdrawal.GetProperty("type").GetString());
        Assert.Equal("سحب رصيد: 1000 نقطة (90.00 جنيه عبر إنستاباي)", withdrawal.GetProperty("description").GetString());
    }

    [Fact]
    public async Task Gift_history_carries_the_novel_and_counts_only_what_it_lists()
    {
        var reader = await api.SignUp();
        var author = await api.SignUp();
        var kept = await api.AddNovel(author, title: "رواية باقية " + Seed.Marker());
        var deleted = await api.AddNovel(author);
        await Fund(reader, 2000);
        (await SendGift(reader, kept.Id, 1)).EnsureSuccessStatusCode();
        (await SendGift(reader, deleted.Id, 2)).EnsureSuccessStatusCode();
        await using (var db = api.Db())
        {
            db.NovelPrivilegeSubscriptions.AddRange(
                new NovelPrivilegeSubscription { Id = Guid.NewGuid(), NovelId = kept.Id, UserId = reader.Id, AmountPaid = 100, IsActive = true },
                new NovelPrivilegeSubscription { Id = Guid.NewGuid(), NovelId = deleted.Id, UserId = reader.Id, AmountPaid = 100, IsActive = true });
            await db.SaveChangesAsync();
            await db.Novels.Where(n => n.Id == deleted.Id).ExecuteUpdateAsync(s => s.SetProperty(n => n.IsDeleted, true));
        }

        var history = await (await api.Get("/api/gift/my-history", reader)).OkJson();

        // The gift to the deleted novel isn't listed, and isn't counted either (it used to be: totalItemsCount 2).
        Assert.Equal(1, history.GetProperty("totalItemsCount").GetInt32());
        var item = Assert.Single(history.GetProperty("items").EnumerateArray());
        Assert.Equal(kept.Id, item.GetProperty("novelId").GetGuid());
        Assert.Equal(kept.Slug, item.GetProperty("novelSlug").GetString());
        Assert.Equal(kept.Title, item.GetProperty("novelTitle").GetString());
        Assert.Equal(kept.CoverImageUrl, item.GetProperty("novelCoverImageUrl").GetString());
        Assert.Equal("وردة", item.GetProperty("gift").GetProperty("nameAr").GetString());
        Assert.Equal(1, item.GetProperty("count").GetInt32());
        // The sender is the user: the always-null sender fields are gone.
        Assert.False(item.TryGetProperty("senderUserName", out _));
        Assert.False(item.TryGetProperty("senderDisplayName", out _));
        Assert.False(item.TryGetProperty("senderProfilePhoto", out _));

        var subscriptions = await (await api.Get("/api/privilege/my-subscriptions", reader)).OkJson();
        Assert.Equal(1, subscriptions.GetProperty("totalCount").GetInt32());
        Assert.Equal(1, subscriptions.GetProperty("totalPages").GetInt32());
        Assert.Equal(kept.Id, Assert.Single(subscriptions.GetProperty("subscriptions").EnumerateArray()).GetProperty("novelId").GetGuid());
    }
}
