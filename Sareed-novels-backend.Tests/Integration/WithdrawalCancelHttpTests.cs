using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Domain.Constants;
using Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Sareed_novels_backend.Tests.Integration;

/// <summary>
/// A member cancels their own pending withdrawal request (#27): DELETE /api/wallet/withdraw/{id}. A pending request
/// reserves its points, and one that can no longer be paid (made before #22 against bought points, say) used to lock
/// the member out of withdrawing what they earned since (scenario H).
/// </summary>
[Collection(ReaderApiCollection.Name)]
public class WithdrawalCancelHttpTests(SardApiFactory api)
{
    private const string CancelledByOwner = "ألغاه صاحب الطلب";

    private async Task Fund(ApiUser user, decimal balance)
    {
        await using var db = api.Db();
        db.UserWallets.Add(new UserWallet { Id = Guid.NewGuid(), UserId = user.Id, CurrentBalance = balance });
        await db.SaveChangesAsync();
    }

    /// <summary>A request as the database holds it, e.g. one made before #22.</summary>
    private async Task<WithdrawalRequest> SeedRequest(ApiUser user, int points, string status = RequestStatus.Pending, string? reason = null)
    {
        var request = new WithdrawalRequest
        {
            Id = Guid.NewGuid(), UserId = user.Id, PointsRequested = points, WithdrawalMethod = PaymentMethod.InstaPay,
            PaymentDetails = "01000000000", BaseAmountEGP = points / 10m, TaxDeducted = points / 100m, NetAmountEGP = points * 0.09m,
            Status = status, RequestedAt = DateTime.UtcNow.AddDays(-40),
            ProcessedAt = status == RequestStatus.Pending ? null : DateTime.UtcNow.AddDays(-39), RejectionReason = reason
        };
        await using var db = api.Db();
        db.WithdrawalRequests.Add(request);
        await db.SaveChangesAsync();
        return request;
    }

    private async Task<WithdrawalRequest> Stored(Guid id)
    {
        await using var db = api.Db();
        return await db.WithdrawalRequests.AsNoTracking().SingleAsync(r => r.Id == id);
    }

    private Task<HttpResponseMessage> Cancel(ApiUser? user, Guid id) => api.Send(HttpMethod.Delete, $"/api/wallet/withdraw/{id}", user);

    private Task<HttpResponseMessage> Withdraw(ApiUser user, int points) =>
        api.Send(HttpMethod.Post, "/api/wallet/withdraw", user, JsonContent.Create(new
        {
            pointsRequested = points, withdrawalMethod = PaymentMethod.InstaPay, paymentDetails = "01000000000"
        }));

    private async Task<decimal> Withdrawable(ApiUser user) =>
        (await (await api.Get("/api/wallet", user)).OkJson()).GetProperty("withdrawable").GetDecimal();

    [Fact]
    public async Task H_a_member_locked_out_by_a_request_that_cannot_be_paid_cancels_it_and_withdraws_what_they_earned()
    {
        // 5000 topped up, and a request for all of it made before #22: it can never be paid. Since then the author earned
        // 1500, released now; the pending request reserves more than that.
        var author = await api.SignUp();
        await Fund(author, 6500);
        await using (var db = api.Db())
        {
            db.PointTransactions.Add(WalletTesting.ReleasedEarning(author.Id, 1500));
            await db.SaveChangesAsync();
        }
        var stuck = await SeedRequest(author, 5000);
        Assert.Equal(0m, await Withdrawable(author));

        var cancelled = await Cancel(author, stuck.Id);

        Assert.Equal(HttpStatusCode.NoContent, cancelled.StatusCode);
        Assert.Equal(1500m, await Withdrawable(author));
        (await Withdraw(author, 1500)).EnsureSuccessStatusCode();
        Assert.Equal(0m, await Withdrawable(author));
    }

    [Fact]
    public async Task Cancelling_your_own_pending_request_closes_it_with_the_reason_and_frees_its_points()
    {
        var author = await api.SignUp();
        await Fund(author, 2000);
        await using (var db = api.Db())
        {
            db.PointTransactions.Add(WalletTesting.ReleasedEarning(author.Id, 2000));
            await db.SaveChangesAsync();
        }
        (await Withdraw(author, 1500)).EnsureSuccessStatusCode();
        Assert.Equal(500m, await Withdrawable(author));
        var mine = Assert.Single((await (await api.Get("/api/wallet/withdraw", author)).OkJson()).GetProperty("requests").EnumerateArray());
        var id = mine.GetProperty("id").GetGuid();

        var response = await Cancel(author, id);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(2000m, await Withdrawable(author));
        var stored = await Stored(id);
        Assert.Equal((RequestStatus.Rejected, CancelledByOwner, author.Id), (stored.Status, stored.RejectionReason, stored.ProcessedBy));
        Assert.NotNull(stored.ProcessedAt);
        // The member's history shows it closed, with the reason; nothing was ever deducted.
        var listed = Assert.Single((await (await api.Get("/api/wallet/withdraw", author)).OkJson()).GetProperty("requests").EnumerateArray());
        Assert.Equal((RequestStatus.Rejected, CancelledByOwner, true), (listed.GetProperty("status").GetString(),
            listed.GetProperty("rejectionReason").GetString(), listed.GetProperty("cancelledByOwner").GetBoolean()));
        Assert.Equal(2000m, (await (await api.Get("/api/wallet", author)).OkJson()).GetProperty("currentBalance").GetDecimal());
    }

    [Fact]
    public async Task The_admin_is_told_a_request_was_cancelled_by_its_owner_and_it_leaves_the_pending_list()
    {
        var (admin, owner) = (await api.SignUpAdmin(), await api.SignUp());
        var request = await SeedRequest(owner, 1000);
        Assert.Equal(HttpStatusCode.NoContent, (await Cancel(owner, request.Id)).StatusCode);

        var approve = await api.Send(HttpMethod.Patch, $"/api/admin/withdraw/{request.Id}/approve", admin);

        var body = await approve.Error(HttpStatusCode.BadRequest);
        Assert.Equal(("AlreadyProcessed", "ألغى صاحبه هذا الطلب من قبل"), (body.GetProperty("code").GetString(), body.GetProperty("message").GetString()));
        var pending = await (await api.Get("/api/admin/withdraw/pending?pageSize=100", admin)).OkJson();
        Assert.DoesNotContain(pending.GetProperty("requests").EnumerateArray(), r => r.GetProperty("id").GetGuid() == request.Id);
    }

    [Fact]
    public async Task Another_members_request_or_an_unknown_id_is_not_found()
    {
        var (owner, other) = (await api.SignUp(), await api.SignUp());
        var request = await SeedRequest(owner, 1000);

        foreach (var id in new[] { request.Id, Guid.NewGuid() })
        {
            var body = await (await Cancel(other, id)).Error(HttpStatusCode.NotFound);
            Assert.Equal("RequestNotFound", body.GetProperty("code").GetString());
            Assert.Equal("طلب السحب غير موجود", body.GetProperty("message").GetString());
        }
        Assert.Equal(RequestStatus.Pending, (await Stored(request.Id)).Status);
    }

    [Theory]
    [InlineData(RequestStatus.Approved, null, "قُبل طلب السحب هذا من قبل، فلا يمكن إلغاؤه.")]
    [InlineData(RequestStatus.Rejected, "بيانات الاستلام غير صحيحة", "رُفض طلب السحب هذا من قبل، فلا يمكن إلغاؤه.")]
    public async Task A_request_an_admin_decided_cannot_be_cancelled(string status, string? reason, string message)
    {
        var owner = await api.SignUp();
        var request = await SeedRequest(owner, 1000, status, reason);

        var body = await (await Cancel(owner, request.Id)).Error(HttpStatusCode.Conflict);

        Assert.Equal("AlreadyProcessed", body.GetProperty("code").GetString());
        Assert.Equal(message, body.GetProperty("message").GetString());
        var stored = await Stored(request.Id);
        Assert.Equal((status, reason), (stored.Status, stored.RejectionReason));
        var listed = Assert.Single((await (await api.Get("/api/wallet/withdraw", owner)).OkJson()).GetProperty("requests").EnumerateArray());
        Assert.False(listed.GetProperty("cancelledByOwner").GetBoolean());
    }

    [Fact]
    public async Task Cancelling_again_or_several_times_at_once_is_done_once()
    {
        // A retry after a lost answer, or a double tap: the request is cancelled, so it is done (as with the other
        // idempotent writes).
        var owner = await api.SignUp();
        var request = await SeedRequest(owner, 1000);

        var responses = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => Cancel(owner, request.Id)));
        var closed = await Stored(request.Id);
        var again = await Cancel(owner, request.Id);

        Assert.All(responses.Append(again), r => Assert.Equal(HttpStatusCode.NoContent, r.StatusCode));
        var stored = await Stored(request.Id);
        Assert.Equal((RequestStatus.Rejected, CancelledByOwner, owner.Id), (stored.Status, stored.RejectionReason, stored.ProcessedBy));
        Assert.Equal(closed.ProcessedAt, stored.ProcessedAt); // closed once
    }

    [Fact]
    public async Task Cancelling_needs_a_signed_in_member()
    {
        var owner = await api.SignUp();
        var request = await SeedRequest(owner, 1000);

        Assert.Equal(HttpStatusCode.Unauthorized, (await Cancel(null, request.Id)).StatusCode);
        Assert.Equal(RequestStatus.Pending, (await Stored(request.Id)).Status);
    }
}
