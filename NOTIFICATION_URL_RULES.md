# Notification URL Rules

## 📋 Simple & Clean Rules

All comment/reply-related notifications follow these **2 simple rules**:

### ✅ Rule 1: Chapter Comments/Replies
**If comment/reply is on a chapter** → Link to chapter page
```
/novel/{novelSlug}/chapter/{chapterId}
```

### ✅ Rule 2: Post Comments/Replies  
**If comment/reply is on a post** → Link to user profile
```
/profile/{username}
```

---

## 📊 Notification Types Coverage

| Notification Type | Context | URL Pattern |
|-------------------|---------|-------------|
| **Reply to Comment** | Chapter | `/novel/{slug}/chapter/{id}` |
| **Reply to Comment** | Post | `/profile/{username}` |
| **Like on Comment** | Chapter | `/novel/{slug}/chapter/{id}` |
| **Like on Comment** | Post | `/profile/{username}` |
| **Comment on Chapter** | Chapter | `/novel/{slug}/chapter/{id}` |
| **Comment on Post** | Post | `/profile/{username}` |

---

## 🔧 Implementation Pattern

All methods follow the same clean pattern:

```csharp
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
```

---

## ✅ Methods Updated

1. `SendReplyToCommentNotification` - ✅ Uses context
2. `SendLikeOnCommentNotification` - ✅ Uses context
3. `SendCommentOnChapterNotification` - ✅ Already correct
4. `SendCommentOnPostNotification` - ✅ Already correct

---

## 🎯 Why This is Clean

1. **No intermediate redirects** - Direct navigation
2. **Consistent pattern** - Same logic everywhere
3. **No frontend complexity** - No comment anchors needed
4. **Simple rules** - Just 2 cases: chapter or post
5. **Easy to maintain** - One pattern for all comment notifications

---

## 📝 Frontend Behavior

When user clicks notification:
1. **Chapter notifications**: Goes to chapter page, user scrolls to find comment
2. **Post notifications**: Goes to user profile with posts visible

No need for:
- ❌ Comment-specific URLs with hashes
- ❌ Server-side rendering for anchors
- ❌ Complex redirect logic
- ❌ Additional API calls

Just **clean, direct navigation**! 🚀
