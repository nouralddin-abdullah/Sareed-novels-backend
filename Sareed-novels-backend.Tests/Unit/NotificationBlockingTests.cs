using System.Reflection;
using Domain.Constants;

namespace Sareed_novels_backend.Tests.Unit;

/// <summary>
/// Which blocks stop each notification type (#52). A type added to <see cref="NotificationType"/> fails
/// <see cref="Every_notification_type_is_in_the_table"/> until someone decides here which block stops it.
/// </summary>
public class NotificationBlockingTests
{
    /// <summary>Every type: true when a block either way stops it, false when only the recipient's does.</summary>
    public static readonly TheoryData<string, bool> Types = new()
    {
        { NotificationType.NewFollower, true },
        { NotificationType.CommentOnChapter, true },
        { NotificationType.CommentOnPost, true },
        { NotificationType.ReplyToComment, true },
        { NotificationType.ReviewOnNovel, true },
        { NotificationType.LikeOnReview, true },
        { NotificationType.LikeOnComment, true },
        { NotificationType.LikeOnPost, true },
        { NotificationType.ReadingListFollowed, true },
        // A new chapter of a novel in the reader's library, and the payments an author should learn of.
        { NotificationType.NewChapterInLibrary, false },
        { NotificationType.GiftReceived, false },
        { NotificationType.PrivilegeSubscribed, false }
    };

    [Theory]
    [MemberData(nameof(Types))]
    public void Each_type_is_stopped_by_its_block(string type, bool eitherWay) =>
        Assert.Equal(eitherWay, NotificationBlocking.EitherWay(type));

    [Fact]
    public void Every_notification_type_is_in_the_table()
    {
        var constants = typeof(NotificationType).GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(f => f.IsLiteral && f.FieldType == typeof(string))
            .Select(f => (string)f.GetRawConstantValue()!);

        Assert.Equal(constants.Order(), Types.Select(row => (string)row[0]).Order());
    }

    [Fact]
    public void A_type_it_does_not_know_is_stopped_either_way() =>
        Assert.True(NotificationBlocking.EitherWay("SomeFutureType"));
}
