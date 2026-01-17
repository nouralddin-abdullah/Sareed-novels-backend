using Application.Services;
using Domain.Constants;
using Domain.Entities;
using Domain.Repositories;
using Microsoft.Extensions.Logging;

namespace Infrastructure.Services;

public class NotificationService(
    ILogger<NotificationService> logger,
    INotificationsRepository notificationsRepository) : INotificationService
{
    public async Task SendNewFollowerNotification(string followedUserId, User follower)
    {
        try
        {
            var notification = new Notification
            {
                Id = Guid.NewGuid(),
                UserId = followedUserId,
                Type = NotificationType.NewFollower,
                ActorId = follower.Id,
                ActorDisplayName = follower.DisplayName,
                ActorProfilePhoto = follower.ProfilePhoto,
                Message = $"{follower.DisplayName} بدأ بمتابعتك",
                ActionUrl = $"/profile/{follower.UserName}",
                IsRead = false,
                CreatedAt = DateTime.UtcNow
            };

            await notificationsRepository.CreateNotification(notification);
            logger.LogDebug("Created NewFollower notification for user {UserId}", followedUserId);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to create NewFollower notification for user {UserId}", followedUserId);
        }
    }

    public async Task SendCommentOnChapterNotification(string authorId, User commenter, Guid commentId, Novel novel, Chapter chapter)
    {
        try
        {
            // Don't notify if author comments on their own chapter
            if (authorId == commenter.Id) return;

            var notification = new Notification
            {
                Id = Guid.NewGuid(),
                UserId = authorId,
                Type = NotificationType.CommentOnChapter,
                ActorId = commenter.Id,
                ActorDisplayName = commenter.DisplayName,
                ActorProfilePhoto = commenter.ProfilePhoto,
                Message = $"{commenter.DisplayName} علق على فصلك '{chapter.Title}'",
                ActionUrl = $"/novel/{novel.Slug}/chapter/{chapter.Id}",
                IsRead = false,
                RelatedEntityId = commentId,
                RelatedEntityType = "Comment",
                CreatedAt = DateTime.UtcNow
            };

            await notificationsRepository.CreateNotification(notification);
            logger.LogDebug("Created CommentOnChapter notification for user {UserId}", authorId);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to create CommentOnChapter notification for user {UserId}", authorId);
        }
    }

    public async Task SendCommentOnPostNotification(string postAuthorId, User commenter, Guid commentId, string postAuthorUsername)
    {
        try
        {
            // Don't notify if author comments on their own post
            if (postAuthorId == commenter.Id) return;

            var notification = new Notification
            {
                Id = Guid.NewGuid(),
                UserId = postAuthorId,
                Type = NotificationType.CommentOnPost,
                ActorId = commenter.Id,
                ActorDisplayName = commenter.DisplayName,
                ActorProfilePhoto = commenter.ProfilePhoto,
                Message = $"{commenter.DisplayName} علق على منشورك",
                ActionUrl = $"/profile/{postAuthorUsername}",
                IsRead = false,
                RelatedEntityId = commentId,
                RelatedEntityType = "Comment",
                CreatedAt = DateTime.UtcNow
            };

            await notificationsRepository.CreateNotification(notification);
            logger.LogDebug("Created CommentOnPost notification for user {UserId}", postAuthorId);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to create CommentOnPost notification for user {UserId}", postAuthorId);
        }
    }

    public async Task SendReplyToCommentNotification(string originalCommentAuthorId, User replier, Guid replyId, Domain.Entities.Comments originalComment, Novel? novel = null, Chapter? chapter = null, string? postAuthorUsername = null)
    {
        try
        {
            // Don't notify if user replies to their own comment
            if (originalCommentAuthorId == replier.Id) return;

            // ✅ Build direct URL to chapter or post (frontend doesn't support comment anchors)
            string actionUrl = "/notifications"; // Default fallback
            
            // For chapter/paragraph comments, link directly to chapter
            if ((originalComment.ChapterId.HasValue || originalComment.ParagraphId.HasValue) && novel != null && chapter != null)
            {
                actionUrl = $"/novel/{novel.Slug}/chapter/{chapter.Id}";
            }
            // For post comments, link to user profile
            else if (originalComment.PostId.HasValue && !string.IsNullOrEmpty(postAuthorUsername))
            {
                actionUrl = $"/profile/{postAuthorUsername}";
            }

            var notification = new Notification
            {
                Id = Guid.NewGuid(),
                UserId = originalCommentAuthorId,
                Type = NotificationType.ReplyToComment,
                ActorId = replier.Id,
                ActorDisplayName = replier.DisplayName,
                ActorProfilePhoto = replier.ProfilePhoto,
                Message = $"{replier.DisplayName} رد على تعليقك",
                ActionUrl = actionUrl,
                IsRead = false,
                RelatedEntityId = replyId,  // Keep reply ID for reference
                RelatedEntityType = "Comment",
                CreatedAt = DateTime.UtcNow
            };

            await notificationsRepository.CreateNotification(notification);
            logger.LogDebug("Created ReplyToComment notification for user {UserId}", originalCommentAuthorId);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to create ReplyToComment notification for user {UserId}", originalCommentAuthorId);
        }
    }

    public async Task SendNewChapterInLibraryNotification(List<string> userIds, Novel novel, Chapter chapter)
    {
        try
        {
            foreach (var userId in userIds)
            {
                var notification = new Notification
                {
                    Id = Guid.NewGuid(),
                    UserId = userId,
                    Type = NotificationType.NewChapterInLibrary,
                    ActorId = novel.Id.ToString(), // Use novel ID instead of author ID
                    ActorDisplayName = novel.Title, // Novel title instead of author name
                    ActorProfilePhoto = novel.CoverImageUrl, // Novel cover instead of author photo
                    Message = $"فصل جديد في '{novel.Title}': {chapter.Title}",
                    ActionUrl = $"/novel/{novel.Slug}/chapter/{chapter.Id}",
                    IsRead = false,
                    RelatedEntityId = chapter.Id,
                    RelatedEntityType = "Chapter",
                    CreatedAt = DateTime.UtcNow
                };

                await notificationsRepository.CreateNotification(notification);
            }

            logger.LogDebug("Created NewChapterInLibrary notifications for {Count} users", userIds.Count);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to create NewChapterInLibrary notifications");
        }
    }

    public async Task SendReviewOnNovelNotification(string novelAuthorId, User reviewer, Guid reviewId, Novel novel)
    {
        try
        {
            // Don't notify if author reviews their own novel
            if (novelAuthorId == reviewer.Id) return;

            var notification = new Notification
            {
                Id = Guid.NewGuid(),
                UserId = novelAuthorId,
                Type = NotificationType.ReviewOnNovel,
                ActorId = reviewer.Id,
                ActorDisplayName = reviewer.DisplayName,
                ActorProfilePhoto = reviewer.ProfilePhoto,
                Message = $"{reviewer.DisplayName} قيّم روايتك '{novel.Title}'",
                ActionUrl = $"/novel/{novel.Slug}",
                IsRead = false,
                RelatedEntityId = reviewId,
                RelatedEntityType = "Review",
                CreatedAt = DateTime.UtcNow
            };

            await notificationsRepository.CreateNotification(notification);
            logger.LogDebug("Created ReviewOnNovel notification for user {UserId}", novelAuthorId);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to create ReviewOnNovel notification for user {UserId}", novelAuthorId);
        }
    }

    // Phase 2: Like notifications
    public async Task SendLikeOnPostNotification(string postAuthorId, User liker, string postAuthorUsername)
    {
        try
        {
            // Don't notify if user likes their own post
            if (postAuthorId == liker.Id) return;

            var notification = new Notification
            {
                Id = Guid.NewGuid(),
                UserId = postAuthorId,
                Type = NotificationType.LikeOnPost,
                ActorId = liker.Id,
                ActorDisplayName = liker.DisplayName,
                ActorProfilePhoto = liker.ProfilePhoto,
                Message = $"{liker.DisplayName} أعجب بمنشورك",
                ActionUrl = $"/profile/{postAuthorUsername}",
                IsRead = false,
                RelatedEntityId = null,
                RelatedEntityType = "Post",
                CreatedAt = DateTime.UtcNow
            };

            await notificationsRepository.CreateNotification(notification);
            logger.LogDebug("Created LikeOnPost notification for user {UserId}", postAuthorId);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to create LikeOnPost notification for user {UserId}", postAuthorId);
        }
    }

    public async Task SendLikeOnCommentNotification(string commentAuthorId, User liker, Guid commentId, Domain.Entities.Comments comment, Novel? novel = null, Chapter? chapter = null, string? postAuthorUsername = null)
    {
        try
        {
            // Don't notify if user likes their own comment
            if (commentAuthorId == liker.Id) return;

            // ✅ Build direct URL: chapter comment → chapter, post comment → profile
            string actionUrl = "/notifications"; // Default fallback
            
            // For chapter/paragraph comments, link directly to chapter
            if ((comment.ChapterId.HasValue || comment.ParagraphId.HasValue) && novel != null && chapter != null)
            {
                actionUrl = $"/novel/{novel.Slug}/chapter/{chapter.Id}";
            }
            // For post comments, link to user profile
            else if (comment.PostId.HasValue && !string.IsNullOrEmpty(postAuthorUsername))
            {
                actionUrl = $"/profile/{postAuthorUsername}";
            }

            var notification = new Notification
            {
                Id = Guid.NewGuid(),
                UserId = commentAuthorId,
                Type = NotificationType.LikeOnComment,
                ActorId = liker.Id,
                ActorDisplayName = liker.DisplayName,
                ActorProfilePhoto = liker.ProfilePhoto,
                Message = $"{liker.DisplayName} أعجب بتعليقك",
                ActionUrl = actionUrl,
                IsRead = false,
                RelatedEntityId = commentId,
                RelatedEntityType = "Comment",
                CreatedAt = DateTime.UtcNow
            };

            await notificationsRepository.CreateNotification(notification);
            logger.LogDebug("Created LikeOnComment notification for user {UserId}", commentAuthorId);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to create LikeOnComment notification for user {UserId}", commentAuthorId);
        }
    }

    public async Task SendLikeOnReviewNotification(string reviewAuthorId, User liker, Guid reviewId, Novel novel)
    {
        try
        {
            // Don't notify if user likes their own review
            if (reviewAuthorId == liker.Id) return;

            var notification = new Notification
            {
                Id = Guid.NewGuid(),
                UserId = reviewAuthorId,
                Type = NotificationType.LikeOnReview,
                ActorId = liker.Id,
                ActorDisplayName = liker.DisplayName,
                ActorProfilePhoto = liker.ProfilePhoto,
                Message = $"{liker.DisplayName} أعجب بتقييمك لرواية '{novel.Title}'",
                ActionUrl = $"/novel/{novel.Slug}",
                IsRead = false,
                RelatedEntityId = reviewId,
                RelatedEntityType = "Review",
                CreatedAt = DateTime.UtcNow
            };

            await notificationsRepository.CreateNotification(notification);
            logger.LogDebug("Created LikeOnReview notification for user {UserId}", reviewAuthorId);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to create LikeOnReview notification for user {UserId}", reviewAuthorId);
        }
    }

    public async Task SendReadingListFollowedNotification(string listOwnerId, User follower, Guid readingListId, string readingListName)
    {
        try
        {
            // Don't notify if user follows their own reading list
            if (listOwnerId == follower.Id) return;

            var notification = new Notification
            {
                Id = Guid.NewGuid(),
                UserId = listOwnerId,
                Type = NotificationType.ReadingListFollowed,
                ActorId = follower.Id,
                ActorDisplayName = follower.DisplayName,
                ActorProfilePhoto = follower.ProfilePhoto,
                Message = $"{follower.DisplayName} تابع قائمة القراءة '{readingListName}'",
                ActionUrl = $"/reading-list/{readingListId}",
                IsRead = false,
                RelatedEntityId = readingListId,
                RelatedEntityType = "ReadingList",
                CreatedAt = DateTime.UtcNow
            };

            await notificationsRepository.CreateNotification(notification);
            logger.LogDebug("Created ReadingListFollowed notification for user {UserId}", listOwnerId);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to create ReadingListFollowed notification for user {UserId}", listOwnerId);
        }
    }

    public async Task SendGiftReceivedNotification(string novelAuthorId, User sender, Novel novel, Gift gift, int count)
    {
        try
        {
            var notification = new Notification
            {
                Id = Guid.NewGuid(),
                UserId = novelAuthorId,
                Type = NotificationType.GiftReceived,
                ActorId = sender.Id,
                ActorDisplayName = sender.DisplayName,
                ActorProfilePhoto = sender.ProfilePhoto,
                Message = $"{sender.DisplayName} أرسل لك {count}x {gift.Name} على رواية '{novel.Title}'",
                ActionUrl = $"/novel/{novel.Slug}",
                IsRead = false,
                RelatedEntityId = novel.Id,
                RelatedEntityType = "Gift",
                CreatedAt = DateTime.UtcNow
            };

            await notificationsRepository.CreateNotification(notification);
            logger.LogDebug("Created GiftReceived notification for user {UserId}", novelAuthorId);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to create GiftReceived notification for user {UserId}", novelAuthorId);
        }
    }

    // ✅ NEW: Privilege subscription notification
    public async Task SendPrivilegeSubscribedNotification(string novelAuthorId, User subscriber, Novel novel, decimal cost)
    {
        try
        {
            var notification = new Notification
            {
                Id = Guid.NewGuid(),
                UserId = novelAuthorId,
                Type = NotificationType.PrivilegeSubscribed,
                ActorId = subscriber.Id,
                ActorDisplayName = subscriber.DisplayName,
                ActorProfilePhoto = subscriber.ProfilePhoto,
                Message = $"{subscriber.DisplayName} اشترك في نظام الامتياز لرواية '{novel.Title}' ({cost} نقطة)",
                ActionUrl = $"/novel/{novel.Slug}",
                IsRead = false,
                RelatedEntityId = novel.Id,
                RelatedEntityType = "Privilege",
                CreatedAt = DateTime.UtcNow
            };

            await notificationsRepository.CreateNotification(notification);
            logger.LogDebug("Created PrivilegeSubscribed notification for user {UserId}", novelAuthorId);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to create PrivilegeSubscribed notification for user {UserId}", novelAuthorId);
        }
    }
}
