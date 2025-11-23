using Domain.Entities;

namespace Application.Services;

public interface INotificationService
{
    Task SendNewFollowerNotification(string followedUserId, User follower);
    Task SendCommentOnChapterNotification(string authorId, User commenter, Guid commentId, Novel novel, Chapter chapter);
    Task SendCommentOnPostNotification(string postAuthorId, User commenter, Guid commentId, string postAuthorUsername);
    Task SendReplyToCommentNotification(string originalCommentAuthorId, User replier, Guid replyId, Domain.Entities.Comments originalComment);
    Task SendNewChapterInLibraryNotification(List<string> userIds, Novel novel, Chapter chapter);
    Task SendReviewOnNovelNotification(string novelAuthorId, User reviewer, Guid reviewId, Novel novel);
    Task SendGiftReceivedNotification(string novelAuthorId, User sender, Novel novel, Gift gift, int count);
    
    // Phase 2: Like notifications
    Task SendLikeOnPostNotification(string postAuthorId, User liker, string postAuthorUsername);
    Task SendLikeOnCommentNotification(string commentAuthorId, User liker, Guid commentId, Domain.Entities.Comments comment);
    Task SendLikeOnReviewNotification(string reviewAuthorId, User liker, Guid reviewId, Novel novel);
    Task SendReadingListFollowedNotification(string listOwnerId, User follower, Guid readingListId, string readingListName);
}
