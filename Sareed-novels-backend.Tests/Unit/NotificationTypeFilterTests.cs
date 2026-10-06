using System.Reflection;
using Application.Notifications;
using Domain.Constants;

namespace Sareed_novels_backend.Tests.Unit;

/// <summary>
/// The types filter of GET /api/notifications and /unread-count (#78): known names in any letter case and with spaces
/// around them, unknown ones ignored, and no known name at all meaning every type; for PATCH /api/notifications/read
/// (#92), none. Through the API: Integration/NotificationTypesHttpTests and NotificationReadByTypesHttpTests.
/// </summary>
public class NotificationTypeFilterTests
{
    [Fact]
    public void All_lists_every_notification_type()
    {
        var constants = typeof(NotificationType).GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(f => f.IsLiteral && f.FieldType == typeof(string))
            .Select(f => (string)f.GetRawConstantValue()!);

        Assert.Equal(constants.Order(), NotificationType.All.Order());
        Assert.Equal(NotificationType.All.Count, NotificationType.All.Distinct().Count());
    }

    [Theory]
    [InlineData("CommentOnChapter,ReviewOnNovel,GiftReceived,PrivilegeSubscribed")]
    [InlineData(" commentonchapter , REVIEWONNOVEL,giftReceived ,  PrivilegeSubscribed  ")]
    [InlineData("CommentOnChapter,,ReviewOnNovel,GiftReceived,PrivilegeSubscribed,")]
    [InlineData("CommentOnChapter,NotAType,ReviewOnNovel,GiftReceived,Comment On Chapter,PrivilegeSubscribed")]
    [InlineData("CommentOnChapter,ReviewOnNovel,commentonchapter,GiftReceived,PrivilegeSubscribed,GIFTRECEIVED")]
    public void Known_names_match_in_any_case_with_spaces_around_once_each_and_unknown_ones_are_ignored(string types) =>
        Assert.Equal(
            [NotificationType.CommentOnChapter, NotificationType.ReviewOnNovel, NotificationType.GiftReceived, NotificationType.PrivilegeSubscribed],
            NotificationTypeFilter.Parse([types]));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData(",")]
    [InlineData(" , ,")]
    [InlineData("NotAType")]
    [InlineData("NotAType,AnotherOne")]
    [InlineData("Comment On Chapter")]
    public void No_known_name_means_every_type(string? types)
    {
        Assert.Null(NotificationTypeFilter.Parse([types]));
        Assert.Null(NotificationTypeFilter.Parse(null));
        Assert.Null(NotificationTypeFilter.Parse([]));
    }

    [Fact]
    public void A_repeated_parameter_adds_its_names()
    {
        Assert.Equal(
            [NotificationType.NewFollower, NotificationType.LikeOnPost, NotificationType.ReplyToComment],
            NotificationTypeFilter.Parse(["newfollower,LikeOnPost", "ReplyToComment", "NotAType", "likeonpost"]));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" , ,")]
    [InlineData("NotAType")]
    [InlineData("NotAType,AnotherOne")]
    [InlineData("Comment On Chapter")]
    public void Marking_read_never_widens_to_every_type(string? types)
    {
        // What a list reads as every type, marking read (#92) reads as none.
        Assert.Empty(NotificationTypeFilter.Exactly([types]));
        Assert.Empty(NotificationTypeFilter.Exactly(null));
        Assert.Empty(NotificationTypeFilter.Exactly([]));
    }

    [Theory]
    [InlineData("CommentOnChapter,ReviewOnNovel,GiftReceived,PrivilegeSubscribed")]
    [InlineData(" commentonchapter , REVIEWONNOVEL,giftReceived ,  PrivilegeSubscribed  ")]
    [InlineData("CommentOnChapter,NotAType,ReviewOnNovel,GiftReceived,Comment On Chapter,PrivilegeSubscribed,GIFTRECEIVED")]
    public void Marking_read_reads_known_names_as_a_list_does(string types)
    {
        Assert.Equal(
            [NotificationType.CommentOnChapter, NotificationType.ReviewOnNovel, NotificationType.GiftReceived, NotificationType.PrivilegeSubscribed],
            NotificationTypeFilter.Exactly([types]));
        Assert.Equal(NotificationTypeFilter.Parse([types]), NotificationTypeFilter.Exactly([types]));
    }

    [Fact]
    public void Every_type_can_be_named()
    {
        foreach (var type in NotificationType.All)
        {
            Assert.Equal([type], NotificationTypeFilter.Parse([type.ToLowerInvariant()]));
        }
        Assert.Equal(NotificationType.All, NotificationTypeFilter.Parse([string.Join(",", NotificationType.All)]));
    }
}
