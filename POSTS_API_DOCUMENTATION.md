# Posts API Documentation

## Endpoints

### 1. Create Post
**POST** `/api/posts`

**Auth:** Required

**Content-Type:** `multipart/form-data`

**Parameters:**
| Field | Type | Required | Validation |
|-------|------|----------|------------|
| content | string | Yes | Max 5000 chars |
| image | file | No | Image file |
| novelId | guid | No | Must be valid novel ID |

**Response 200:**
```json
{
  "success": true,
  "message": "Post created successfully"
}
```

**Response 400:**
```json
{
  "success": false,
  "message": "Novel not found"
}
```

---

### 2. Get Single Post
**GET** `/api/posts/{postId}`

**Auth:** Optional

**Response 200:**
```json
{
  "id": "guid",
  "user": {
    "id": "string",
    "displayName": "string",
    "userName": "string",
    "profilePhoto": "string|null"
  },
  "content": "string",
  "imageUrl": "string|null",
  "novel": {
    "id": "guid",
    "title": "string",
    "slug": "string",
    "coverImageUrl": "string",
    "totalAverageScore": 4.5,
    "reviewCount": 127
  } | null,
  "createdAt": "datetime",
  "likesCount": 42,
  "commentsCount": 10,
  "isLikedByCurrentUser": false
}
```

**Response 404:** Post not found

---

### 3. Get User Posts
**GET** `/api/posts/user/{userId}`

**Auth:** Optional

**Query Parameters:**
| Parameter | Type | Required | Default |
|-----------|------|----------|---------|
| pageSize | int | No | 10 |
| pageNumber | int | No | 1 |

**Response 200:**
```json
{
  "items": [
    {
      "id": "guid",
      "user": { ... },
      "content": "string",
      "imageUrl": "string|null",
      "novel": { ... } | null,
      "createdAt": "datetime",
      "likesCount": 0,
      "commentsCount": 0,
      "isLikedByCurrentUser": false
    }
  ],
  "totalPages": 5,
  "totalItemsCount": 50,
  "itemsFrom": 1,
  "itemsTo": 10
}
```

---

### 4. Delete Post
**DELETE** `/api/posts/{postId}`

**Auth:** Required (Owner only)

**Response 204:** Success (No Content)

**Response 400:** Post not found or unauthorized

---

### 5. Like Post
**POST** `/api/posts/{postId}/like`

**Auth:** Required

**Response 200:**
```json
{
  "success": true,
  "message": "Post liked successfully"
}
```

**Response 400:**
```json
{
  "success": false,
  "message": "Already liked this post"
}
```

---

### 6. Unlike Post
**DELETE** `/api/posts/{postId}/unlike`

**Auth:** Required

**Response 200:**
```json
{
  "success": true,
  "message": "Post unliked successfully"
}
```

**Response 400:**
```json
{
  "success": false,
  "message": "Post not liked yet"
}
```

---

## Post Comments

### 7. Create Comment on Post
**POST** `/api/comment/post/{postId}`

**Auth:** Required

**Content-Type:** `multipart/form-data`

**Parameters:**
| Field | Type | Required |
|-------|------|----------|
| content | string | Yes |
| attachedImage | file | No |
| parentCommentId | guid | No |

**Response 200:**
```json
{
  "success": true,
  "message": "Comment created successfully"
}
```

---

### 8. Get Post Comments
**GET** `/api/comment/post/{postId}`

**Auth:** Optional

**Query Parameters:**
| Parameter | Type | Required | Default | Options |
|-----------|------|----------|---------|---------|
| pageSize | int | No | 10 | - |
| pageNumber | int | No | 1 | - |
| sorting | string | No | recent | recent, oldest, popular |

**Response 200:**
```json
{
  "items": [
    {
      "id": "guid",
      "user": {
        "id": "string",
        "displayName": "string",
        "userName": "string",
        "profilePhoto": "string|null"
      },
      "content": "string",
      "attachedImageUrl": "string|null",
      "likesCount": 5,
      "createdAt": "datetime",
      "isLikedByCurrentUser": false,
      "totalRepliesCount": 2,
      "hasMoreReplies": true
    }
  ],
  "totalPages": 3,
  "totalItemsCount": 25,
  "itemsFrom": 1,
  "itemsTo": 10
}
```

---

### 9. Get Comment Replies
**GET** `/api/comment/chapter/comments/{parentCommentId}`

**Auth:** Optional

**Query Parameters:**
| Parameter | Type | Required | Default | Options |
|-----------|------|----------|---------|---------|
| pageSize | int | No | 10 | - |
| pageNumber | int | No | 1 | - |
| sorting | string | No | oldest | recent, oldest, mostliked |

**Response 200:**
```json
{
  "items": [
    {
      "id": "guid",
      "user": { ... },
      "content": "string",
      "attachedImageUrl": "string|null",
      "likesCount": 2,
      "createdAt": "datetime",
      "isLikedByCurrentUser": false
    }
  ],
  "totalPages": 1,
  "totalItemsCount": 5,
  "itemsFrom": 1,
  "itemsTo": 5
}
```

---

### 10. Like Comment
**POST** `/api/comment/{commentId}/like`

**Auth:** Required

**Response 200:**
```json
{
  "success": true,
  "message": "Comment liked successfully"
}
```

---

### 11. Unlike Comment
**DELETE** `/api/comment/{commentId}/unlike`

**Auth:** Required

**Response 200:**
```json
{
  "success": true,
  "message": "Comment unliked successfully"
}
```

---

### 12. Delete Comment
**DELETE** `/api/comment/{commentId}`

**Auth:** Required (Owner only)

**Response 204:** Success (No Content)

**Response 400:** Unauthorized or comment not found

---

## Notes

- **Soft Delete:** Deleted posts remain in database but filtered from queries
- **Novel Attachment:** Optional reference to a novel (null if not attached)
- **Image Upload:** Stored in Cloudflare R2
- **Anonymous Access:** GET endpoints work without authentication, but `isLikedByCurrentUser` will be `false`
- **Owner Authorization:** Only post owner can delete their posts
- **Comment Nesting:** Use `parentCommentId` for replies
- **Comment Replies:** Work the same for posts, chapters, and paragraphs
