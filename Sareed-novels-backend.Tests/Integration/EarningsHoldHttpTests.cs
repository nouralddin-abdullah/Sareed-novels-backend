using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Application.Services;
using Domain.Constants;
using Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Sareed_novels_backend.Tests.Integration;

/// <summary>
/// Payouts (#22) as the web and apps see them: the wallet's withdrawable and pending earnings, the Arabic refusal, what
/// the admin's payout review shows, and deleting an account whose earnings are on hold.
/// </summary>
[Collection(ReaderApiCollection.Name)]
public class EarningsHoldHttpTests(SardApiFactory api)
{
    // Production's catalog: a rose costs 100 points.
    private static readonly Guid Rose = Guid.Parse("ec16dfde-71b8-4e23-8ff5-d1846cdf2036");

    private async Task Fund(ApiUser user, decimal balance)
    {
        await using var db = api.Db();
        db.UserWallets.Add(new UserWallet { Id = Guid.NewGuid(), UserId = user.Id, CurrentBalance = balance });
        await db.SaveChangesAsync();
    }

    private Task<HttpResponseMessage> Withdraw(ApiUser user, int points) =>
        api.Send(HttpMethod.Post, "/api/wallet/withdraw", user, JsonContent.Create(new
        {
            pointsRequested = points, withdrawalMethod = PaymentMethod.InstaPay, paymentDetails = "01000000000"
        }));

    private async Task<JsonElement> Wallet(ApiUser user) => await (await api.Get("/api/wallet", user)).OkJson();

    [Fact]
    public async Task The_wallet_says_what_can_be_withdrawn_and_what_is_on_hold_until_when()
    {
        var (reader, author) = (await api.SignUp(), await api.SignUp());
        var novel = await api.AddNovel(author);
        await Fund(reader, 1000);
        var sentAt = DateTime.UtcNow;
        (await api.Send(HttpMethod.Post, "/api/gift/send", reader, JsonContent.Create(new { giftId = Rose, novelId = novel.Id, count = 3 })))
            .EnsureSuccessStatusCode();

        var earned = await Wallet(author);
        Assert.Equal(300m, earned.GetProperty("currentBalance").GetDecimal());
        Assert.Equal(0m, earned.GetProperty("withdrawable").GetDecimal());
        Assert.Equal(300m, earned.GetProperty("pendingEarnings").GetDecimal());
        Assert.Equal(300m, earned.GetProperty("totalEarned").GetDecimal()); // always 0 before #78
        var nextReleaseAt = earned.GetProperty("nextReleaseAt").GetString()!;
        Assert.EndsWith("Z", nextReleaseAt); // UTC, and says so
        Assert.Equal(sentAt.AddDays(30), DateTime.Parse(nextReleaseAt, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal),
            TimeSpan.FromMinutes(1));

        var bought = await Wallet(reader);
        Assert.Equal(700m, bought.GetProperty("currentBalance").GetDecimal());
        Assert.Equal(0m, bought.GetProperty("withdrawable").GetDecimal());
        Assert.Equal(0m, bought.GetProperty("pendingEarnings").GetDecimal());
        Assert.Equal(0m, bought.GetProperty("totalEarned").GetDecimal());
        Assert.Equal(JsonValueKind.Null, bought.GetProperty("nextReleaseAt").ValueKind);
    }

    [Fact]
    public async Task Withdrawing_bought_points_is_refused_with_a_code_and_an_arabic_message()
    {
        var user = await api.SignUp();
        await Fund(user, 5000);

        var body = await (await Withdraw(user, 1000)).Error(HttpStatusCode.BadRequest);

        Assert.Equal("InsufficientWithdrawableBalance", body.GetProperty("code").GetString());
        Assert.Equal(
            "لا توجد نقاط قابلة للسحب الآن. تُسحب أرباح الهدايا واشتراكات الوصول المبكر وحدها، بعد 30 يومًا من استلامها، أما النقاط المشحونة أو المشتراة فلا تُسحب.",
            body.GetProperty("message").GetString());
        // The minimum is checked first, as before.
        Assert.Equal("BelowMinimumWithdrawal", (await (await Withdraw(user, 999)).Error(HttpStatusCode.BadRequest)).GetProperty("code").GetString());
    }

    [Fact]
    public async Task The_admin_list_shows_what_each_requester_can_be_paid_and_their_recent_reversals()
    {
        var admin = await api.SignUpAdmin();
        var author = await api.SignUp();
        var earning = WalletTesting.ReleasedEarning(author.Id, 1500);
        var now = DateTime.UtcNow;
        PointTransaction Reversal(decimal amount, DateTime at) => new()
        {
            Id = Guid.NewGuid(), UserId = author.Id, Type = TransactionType.EarningReversed, Amount = -amount,
            BalanceBefore = 0, BalanceAfter = -amount, Description = $"أُلغيت أرباح {amount:0} نقطة لأن عملية الشراء التي جاءت منها استُرد مبلغها",
            RelatedRequestId = Guid.NewGuid(), ReversedTransactionId = earning.Id, CreatedAt = at
        };
        var recent = Reversal(200, now.AddDays(-7));
        var old = Reversal(100, now.AddDays(-100));
        await Fund(author, 1200); // 1500 earned, 300 of it taken back
        await using (var db = api.Db())
        {
            db.PointTransactions.AddRange(earning, recent, old);
            await db.SaveChangesAsync();
        }
        Assert.Equal(1200m, (await Wallet(author)).GetProperty("withdrawable").GetDecimal());
        // Earned: 1500, less the 300 taken back.
        Assert.Equal(1200m, (await Wallet(author)).GetProperty("totalEarned").GetDecimal());

        (await Withdraw(author, 1000)).EnsureSuccessStatusCode();
        Assert.Equal(200m, (await Wallet(author)).GetProperty("withdrawable").GetDecimal());

        JsonElement? listed = null;
        for (var page = 1; listed is null; page++)
        {
            var body = await (await api.Get($"/api/admin/withdraw/pending?pageNumber={page}&pageSize=50", admin)).OkJson();
            var requests = body.GetProperty("requests").EnumerateArray().ToList();
            Assert.NotEmpty(requests);
            listed = requests.Cast<JsonElement?>().FirstOrDefault(r => r!.Value.GetProperty("userId").GetString() == author.Id);
        }

        // Approving it can pay 1200: the request's own reservation isn't taken out.
        Assert.Equal(1200m, listed.Value.GetProperty("requesterWithdrawable").GetDecimal());
        var reversal = Assert.Single(listed.Value.GetProperty("recentEarningReversals").EnumerateArray());
        Assert.Equal(recent.Id, reversal.GetProperty("id").GetGuid());
        Assert.Equal(-200m, reversal.GetProperty("amount").GetDecimal());
        Assert.Equal("أُلغيت أرباح 200 نقطة لأن عملية الشراء التي جاءت منها استُرد مبلغها", reversal.GetProperty("description").GetString());
        Assert.Equal(recent.RelatedRequestId, reversal.GetProperty("purchaseId").GetGuid());
        Assert.Equal(earning.Id, reversal.GetProperty("reversedTransactionId").GetGuid());
        Assert.EndsWith("Z", reversal.GetProperty("createdAt").GetString());

        // The member's own history doesn't carry the review's fields.
        var mine = Assert.Single((await (await api.Get("/api/wallet/withdraw", author)).OkJson()).GetProperty("requests").EnumerateArray());
        Assert.False(mine.TryGetProperty("requesterWithdrawable", out _));
        Assert.False(mine.TryGetProperty("recentEarningReversals", out _));

        // Approving pays it.
        var id = listed.Value.GetProperty("id").GetGuid();
        (await api.Send(HttpMethod.Patch, $"/api/admin/withdraw/{id}/approve", admin)).EnsureSuccessStatusCode();
        Assert.Equal(200m, (await Wallet(author)).GetProperty("currentBalance").GetDecimal());
    }

    [Fact]
    public async Task Deleting_an_account_with_earnings_on_hold_forfeits_the_whole_balance_and_cancels_pending_withdrawals()
    {
        var (reader, author) = (await api.SignUp(), await api.SignUp());
        await Fund(author, 1000);
        await using (var db = api.Db())
        {
            db.PointTransactions.Add(WalletTesting.ReleasedEarning(author.Id, 1000));
            await db.SaveChangesAsync();
        }
        (await Withdraw(author, 1000)).EnsureSuccessStatusCode();
        await Fund(reader, 500);
        var novel = await api.AddNovel(author);
        (await api.Send(HttpMethod.Post, "/api/gift/send", reader, JsonContent.Create(new { giftId = Rose, novelId = novel.Id, count = 5 })))
            .EnsureSuccessStatusCode();
        var before = await Wallet(author);
        Assert.Equal((1500m, 0m, 500m), (before.GetProperty("currentBalance").GetDecimal(), before.GetProperty("withdrawable").GetDecimal(),
            before.GetProperty("pendingEarnings").GetDecimal()));

        var deleted = await api.Send(HttpMethod.Delete, "/api/User/me", author, JsonContent.Create(new { password = ModerationApi.Password }));
        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);

        await using (var db = api.Db())
        {
            Assert.Equal(0m, (await db.UserWallets.AsNoTracking().SingleAsync(w => w.UserId == author.Id)).CurrentBalance);
            var ledger = await db.PointTransactions.AsNoTracking().Where(t => t.UserId == author.Id).ToListAsync();
            var forfeit = Assert.Single(ledger, t => t.Type == TransactionType.BalanceForfeited);
            Assert.Equal((-1500m, 1500m, 0m), (forfeit.Amount, forfeit.BalanceBefore, forfeit.BalanceAfter));
            Assert.Null(forfeit.AvailableAt);
            Assert.Equal(0m, ledger.Sum(t => t.Amount));
            // The held earning stays in the ledger, with its release date.
            Assert.NotNull(Assert.Single(ledger, t => t.Type == TransactionType.GiftReceived && t.Amount == 500).AvailableAt);

            var cancelled = await db.WithdrawalRequests.AsNoTracking().SingleAsync(r => r.UserId == author.Id);
            Assert.Equal((RequestStatus.Rejected, DeletedAccounts.WithdrawalCancelledReason), (cancelled.Status, cancelled.RejectionReason));
        }

        // Nothing is left to withdraw, now or when the held earning's date comes.
        using var scope = api.Services.CreateScope();
        var withdrawable = await scope.ServiceProvider.GetRequiredService<IWalletService>().GetWithdrawableAsync(author.Id);
        Assert.Equal((0m, 0m, 0m), (withdrawable.Balance, withdrawable.Withdrawable, withdrawable.Payable));
    }
}
