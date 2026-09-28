using Domain.Constants;
using Domain.Entities;
using Domain.Repositories;
using Infrastructure.Push;
using Infrastructure.Services;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Sareed_novels_backend.Tests.Unit;

/// <summary>
/// The early access notification is also the push body, word for word, and a phone shows a background push itself:
/// it must not name the price. It still starts with the subscriber's name, which account deletion anonymises.
/// </summary>
public class PrivilegeSubscribedNotificationTests
{
    private readonly INotificationsRepository repository = Substitute.For<INotificationsRepository>();

    private readonly User subscriber = new() { Id = "reader-1", UserName = "sara", DisplayName = "سارة", ProfilePhoto = "https://example.test/sara.png" };

    private readonly Novel novel = new()
    {
        Id = Guid.NewGuid(), AuthorId = "author-1", Title = "ظل الأمير", Slug = "1a2b3-ظل-الأمير", Summary = "", CoverImageUrl = ""
    };

    private async Task<Notification> Sent()
    {
        Notification? created = null;
        repository.CreateNotification(Arg.Do<Notification>(n => created = n)).Returns(true);

        await new NotificationService(NullLogger<NotificationService>.Instance, repository)
            .SendPrivilegeSubscribedNotification(novel.AuthorId, subscriber, novel);

        return Assert.IsType<Notification>(created);
    }

    [Fact]
    public async Task The_message_names_the_subscriber_and_the_novel_but_no_price()
    {
        var notification = await Sent();

        Assert.Equal("سارة اشترك في الوصول المبكر لروايتك «ظل الأمير»", notification.Message);
        Assert.DoesNotContain("نقطة", notification.Message);
        Assert.DoesNotMatch(@"\d", notification.Message);
    }

    [Fact]
    public async Task It_starts_with_the_display_name_that_account_deletion_replaces()
    {
        var notification = await Sent();

        Assert.StartsWith(subscriber.DisplayName + " ", notification.Message);
        Assert.Equal(subscriber.DisplayName, notification.ActorDisplayName);
        Assert.Equal(NotificationType.PrivilegeSubscribed, notification.Type);
        Assert.Equal(novel.AuthorId, notification.UserId);
        Assert.Equal(novel.Id, notification.RelatedEntityId);
    }

    [Fact]
    public async Task The_push_body_is_the_message_so_it_names_no_price_either()
    {
        var notification = await Sent();

        var push = PushMessages.Build(notification, new PushTarget { NovelId = novel.Id }, 1, "device-token");

        Assert.Equal(notification.Message, push.Body);
        Assert.DoesNotContain("نقطة", push.Body);
    }
}
