using System.Net;
using System.Text.Json;
using Application.Privileges.Commands.CancelSubscription;
using Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Sareed_novels_backend.Tests.Integration;

/// <summary>
/// A privilege subscription is a permanent unlock (the owner's rule, #17): it can't be cancelled, the privilege info
/// says so (canCancel: false) and carries when the reader subscribed.
/// </summary>
[Collection(ReaderApiCollection.Name)]
public class PrivilegeSubscriptionHttpTests(SardApiFactory api)
{
    private async Task<(ApiUser Reader, Novel Novel)> PrivilegedNovelAndReader(decimal balance = 1000)
    {
        var author = await api.SignUp();
        var reader = await api.SignUp();
        var novel = await api.AddNovel(author);
        await using var db = api.Db();
        db.NovelPrivileges.Add(new NovelPrivilege
        {
            Id = Guid.NewGuid(), NovelId = novel.Id, IsEnabled = true, SubscriptionCost = 150, CurrentLockedCount = 5, PrivilegeStartSequence = 11
        });
        db.UserWallets.Add(new UserWallet { Id = Guid.NewGuid(), UserId = reader.Id, CurrentBalance = balance });
        await db.SaveChangesAsync();
        return (reader, novel);
    }

    private async Task<JsonElement> Info(Guid novelId, ApiUser? user) =>
        await (await api.Get($"/api/novel/{novelId}/privilege", user)).OkJson();

    [Fact]
    public async Task Subscribing_says_so_in_arabic_and_the_info_carries_when_and_that_it_cant_be_cancelled()
    {
        var (reader, novel) = await PrivilegedNovelAndReader();

        var before = await Info(novel.Id, reader);
        Assert.False(before.GetProperty("isSubscribed").GetBoolean());
        Assert.Equal(JsonValueKind.Null, before.GetProperty("subscribedAt").ValueKind);
        Assert.False(before.GetProperty("canCancel").GetBoolean());

        var subscribed = await (await api.Send(HttpMethod.Post, $"/api/novel/{novel.Id}/privilege/subscribe", reader)).OkJson();
        Assert.True(subscribed.GetProperty("success").GetBoolean());
        var message = subscribed.GetProperty("message").GetString()!;
        Assert.Matches(@"\p{IsArabic}", message);
        Assert.DoesNotContain("PERMANENT", message);
        Assert.Contains("150", message);

        var after = await Info(novel.Id, reader);
        Assert.True(after.GetProperty("isSubscribed").GetBoolean());
        Assert.False(after.GetProperty("canCancel").GetBoolean());
        await using var db = api.Db();
        var row = await db.NovelPrivilegeSubscriptions.SingleAsync(s => s.NovelId == novel.Id && s.UserId == reader.Id);
        // subscribedAt used to be null always; now it is the subscription's own date.
        Assert.Equal(row.SubscribedAt, after.GetProperty("subscribedAt").GetDateTime(), TimeSpan.FromMilliseconds(1));

        var anonymous = await Info(novel.Id, null);
        Assert.False(anonymous.GetProperty("isSubscribed").GetBoolean());
        Assert.False(anonymous.GetProperty("canCancel").GetBoolean());
    }

    [Fact]
    public async Task Cancelling_is_refused_with_a_code_and_the_subscription_stays()
    {
        var (reader, novel) = await PrivilegedNovelAndReader();
        (await api.Send(HttpMethod.Post, $"/api/novel/{novel.Id}/privilege/subscribe", reader)).EnsureSuccessStatusCode();

        var refused = await api.Send(HttpMethod.Delete, $"/api/novel/{novel.Id}/privilege/subscription", reader);

        var error = await refused.Error(HttpStatusCode.BadRequest);
        Assert.Equal(CancelSubscriptionCommandHandler.CannotBeCancelledCode, error.GetProperty("code").GetString());
        Assert.Equal(CancelSubscriptionCommandHandler.CannotBeCancelledMessage, error.GetProperty("message").GetString());
        Assert.True((await Info(novel.Id, reader)).GetProperty("isSubscribed").GetBoolean());
        await using var db = api.Db();
        Assert.True((await db.NovelPrivilegeSubscriptions.SingleAsync(s => s.NovelId == novel.Id && s.UserId == reader.Id)).IsActive);

        // Without a subscription too: there is nothing to cancel, ever.
        var (other, _) = await PrivilegedNovelAndReader();
        var nothing = await api.Send(HttpMethod.Delete, $"/api/novel/{novel.Id}/privilege/subscription", other);
        Assert.Equal(CancelSubscriptionCommandHandler.CannotBeCancelledCode, (await nothing.Error(HttpStatusCode.BadRequest)).GetProperty("code").GetString());
    }

    [Fact]
    public async Task A_subscription_cancelled_before_the_rule_stays_cancelled()
    {
        // Production has subscriptions users cancelled while cancelling was offered: they are left as they are.
        var (reader, novel) = await PrivilegedNovelAndReader();
        await using (var db = api.Db())
        {
            db.NovelPrivilegeSubscriptions.Add(new NovelPrivilegeSubscription
            {
                Id = Guid.NewGuid(), NovelId = novel.Id, UserId = reader.Id, AmountPaid = 150, IsActive = false,
                SubscribedAt = DateTime.UtcNow.AddDays(-10), CancelledAt = DateTime.UtcNow.AddDays(-5), CancellationReason = "UserCancelled"
            });
            await db.SaveChangesAsync();
        }

        var info = await Info(novel.Id, reader);
        Assert.False(info.GetProperty("isSubscribed").GetBoolean());
        Assert.Equal(JsonValueKind.Null, info.GetProperty("subscribedAt").ValueKind);
        var mine = await (await api.Get("/api/privilege/my-subscriptions", reader)).OkJson();
        Assert.Equal(0, mine.GetProperty("totalCount").GetInt32());
    }
}
