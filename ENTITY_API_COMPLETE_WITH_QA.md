# Entity System - Complete API Documentation with Frontend Q&A

## ?? **Frontend Questions Answered**

### **Q1: Are there UPDATE/DELETE endpoints for articles?**

**? YES - Now Fully Implemented**

**Update Article:**
- Endpoint: `PATCH /api/novels/{novelId}/entities/articles/{articleId}`
- Update title, content, or order individually
- Only novel owner can update

**Delete Article:**
- Endpoint: `DELETE /api/novels/{novelId}/entities/articles/{articleId}`
- Soft delete (sets `IsDeleted = true`)
- Only novel owner can delete

---

### **Q2: Are there endpoints for relationship management beyond CREATE?**

**? YES - Now Fully Implemented**

**Update Relationship:**
- Endpoint: `PATCH /api/novels/{novelId}/entities/relationships/{relationshipId}`
- Update type, label, or description
- Only novel owner can update

**Delete Relationship:**
- Endpoint: `DELETE /api/novels/{novelId}/entities/relationships/{relationshipId}`
- Soft delete
- Only novel owner can delete

---

### **Q3: Do we need pagination on Wikipedia main page?**

**? ALREADY SUPPORTED**

**List Entities (Paginated):**
```
GET /api/novels/{novelId}/entities?entityType=character&categoryName=Main Heroes&pageNumber=1&pageSize=20
```

**Recommended Approach:**
- **Initial Load:** Fetch first page (20 items) of selected category
- **Scroll/Load More:** Fetch next pages as needed
- **Alternative:** Set high `pageSize` (max 100) to load all at once for small categories

**Response includes:**
```json
{
  "items": [...],
  "totalCount": 42,
  "currentPage": 1,
  "totalPages": 3,
  "pageSize": 20
}
```

---

### **Q4: Should entity images go through gallery upload or separate endpoint?**

**? TWO OPTIONS AVAILABLE:**

**Option A: Direct URL (Current - imageUrl field)**
```json
POST /api/novels/{novelId}/entities
{
  "name": "Aragorn",
  "imageUrl": "https://your-cdn.com/aragorn.jpg"  // ? You provide URL
}
```
- Upload image separately (to Cloudflare, your CDN, etc.)
- Provide URL in entity creation
- **Faster** for single main image
- Good if you handle uploads on frontend

**Option B: Use Gallery System**
```json
// 1. Create entity without imageUrl
POST /api/novels/{novelId}/entities
{
  "name": "Aragorn"
}

// 2. Upload image through gallery
POST /api/novels/{novelId}/entities/{entityId}/gallery
FormData: imageFile=aragorn.jpg, orderIndex=0

// 3. Set first gallery image as main (frontend logic)
```
- **Recommended** if you want unified image management
- All images stored in same place
- Easier to manage/replace

**Recommendation:**
Use **Option A** for main profile image (faster, simpler)
Use **Gallery** for additional images (concept art, scenes, etc.)

---

## ?? **Complete Endpoint Reference**

### **Entity Management**

| Method | Endpoint | Auth | Description |
|--------|----------|------|-------------|
| POST | `/api/novels/{novelId}/entities` | Required | Create entity |
| PATCH | `/api/novels/{novelId}/entities/{entityId}` | Required | Update entity |
| DELETE | `/api/novels/{novelId}/entities/{entityId}` | Required | Delete entity |
| GET | `/api/novels/{novelId}/entities` | Optional | List entities (paginated) |
| GET | `/api/novels/{novelId}/entities/{entityId}` | Optional | Get entity details |
| GET | `/api/novels/{novelId}/entities/search` | Optional | Search entities |
| GET | `/api/novels/{novelId}/entities/categories` | Optional | Get categories |

---

### **Articles Management (NEW)**

| Method | Endpoint | Auth | Description |
|--------|----------|------|-------------|
| POST | `/api/novels/{novelId}/entities/{entityId}/articles` | Required | Add article |
| PATCH | `/api/novels/{novelId}/entities/articles/{articleId}` | Required | **Update article** |
| DELETE | `/api/novels/{novelId}/entities/articles/{articleId}` | Required | **Delete article** |

---

### **Gallery Management**

| Method | Endpoint | Auth | Description |
|--------|----------|------|-------------|
| POST | `/api/novels/{novelId}/entities/{entityId}/gallery` | Required | Upload image |
| DELETE | `/api/novels/{novelId}/entities/gallery/{imageId}` | Required | Remove image |

---

### **Relationships Management (UPDATED)**

| Method | Endpoint | Auth | Description |
|--------|----------|------|-------------|
| POST | `/api/novels/{novelId}/entities/relationships` | Required | Create relationship |
| PATCH | `/api/novels/{novelId}/entities/relationships/{relationshipId}` | Required | **Update relationship** |
| DELETE | `/api/novels/{novelId}/entities/relationships/{relationshipId}` | Required | **Delete relationship** |

---

## ?? **New Endpoint Details**

### **1. Update Article**

**Endpoint:** `PATCH /api/novels/{novelId}/entities/articles/{articleId}`

**Authorization:** Required (Novel owner only)

**Request Body:** (All fields optional)
```json
{
  "title": "string (optional, max 200 chars)",
  "content": "string (optional)",
  "orderIndex": "integer (optional)"
}
```

**Response:** `200 OK`
```json
{
  "success": true,
  "message": "Article updated successfully"
}
```

**Example:**
```json
PATCH /api/novels/{novelId}/entities/articles/123e4567

{
  "title": "Updated Backstory Title",
  "content": "New updated content...",
  "orderIndex": 1
}
```

**Errors:**
- `400 Bad Request` - Validation errors
- `401 Unauthorized` - Not authenticated
- `403 Forbidden` - Not novel owner
- `404 Not Found` - Article not found

---

### **2. Delete Article**

**Endpoint:** `DELETE /api/novels/{novelId}/entities/articles/{articleId}`

**Authorization:** Required (Novel owner only)

**Response:** `204 No Content`

**Example:**
```bash
DELETE /api/novels/{novelId}/entities/articles/123e4567
```

**Errors:**
- `401 Unauthorized` - Not authenticated
- `403 Forbidden` - Not novel owner
- `404 Not Found` - Article not found

---

### **3. Update Relationship**

**Endpoint:** `PATCH /api/novels/{novelId}/entities/relationships/{relationshipId}`

**Authorization:** Required (Novel owner only)

**Request Body:** (All fields optional)
```json
{
  "relationType": "string (optional, max 50 chars)",
  "label": "string (optional, max 100 chars)",
  "description": "string (optional, max 1000 chars)"
}
```

**Response:** `200 OK`
```json
{
  "success": true,
  "message": "Relationship updated successfully"
}
```

**Example:**
```json
PATCH /api/novels/{novelId}/entities/relationships/789abcde

{
  "relationType": "sworn-brothers",
  "label": "Sworn Brothers",
  "description": "Bound by oath and brotherhood"
}
```

**Errors:**
- `400 Bad Request` - Validation errors
- `401 Unauthorized` - Not authenticated
- `403 Forbidden` - Not novel owner
- `404 Not Found` - Relationship not found

---

### **4. Delete Relationship**

**Endpoint:** `DELETE /api/novels/{novelId}/entities/relationships/{relationshipId}`

**Authorization:** Required (Novel owner only)

**Response:** `204 No Content`

**Example:**
```bash
DELETE /api/novels/{novelId}/entities/relationships/789abcde
```

**Errors:**
- `401 Unauthorized` - Not authenticated
- `403 Forbidden` - Not novel owner
- `404 Not Found` - Relationship not found

---

## ?? **Common Frontend Workflows**

### **Workflow 1: Managing Articles**

```typescript
// 1. Add article
const addArticle = async (entityId: string, article: Article) => {
  return await fetch(`/api/novels/${novelId}/entities/${entityId}/articles`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({
      title: article.title,
      content: article.content,
      orderIndex: article.orderIndex
    })
  });
};

// 2. Update article
const updateArticle = async (articleId: string, updates: Partial<Article>) => {
  return await fetch(`/api/novels/${novelId}/entities/articles/${articleId}`, {
    method: 'PATCH',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(updates) // Only send changed fields
  });
};

// 3. Delete article
const deleteArticle = async (articleId: string) => {
  return await fetch(`/api/novels/${novelId}/entities/articles/${articleId}`, {
    method: 'DELETE'
  });
};

// 4. Reorder articles (update orderIndex for each)
const reorderArticles = async (articles: Article[]) => {
  await Promise.all(
    articles.map((article, index) =>
      updateArticle(article.id, { orderIndex: index })
    )
  );
};
```

---

### **Workflow 2: Managing Relationships**

```typescript
// 1. Create relationship
const createRelationship = async (relationship: Relationship) => {
  return await fetch(`/api/novels/${novelId}/entities/relationships`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({
      sourceEntityId: relationship.sourceEntityId,
      targetEntityId: relationship.targetEntityId,
      relationType: relationship.type,
      label: relationship.label,
      description: relationship.description
    })
  });
};

// 2. Update relationship
const updateRelationship = async (relationshipId: string, updates: Partial<Relationship>) => {
  return await fetch(`/api/novels/${novelId}/entities/relationships/${relationshipId}`, {
    method: 'PATCH',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({
      relationType: updates.type,
      label: updates.label,
      description: updates.description
    })
  });
};

// 3. Delete relationship
const deleteRelationship = async (relationshipId: string) => {
  return await fetch(`/api/novels/${novelId}/entities/relationships/${relationshipId}`, {
    method: 'DELETE'
  });
};
```

---

### **Workflow 3: Wikipedia Page with Pagination**

```typescript
interface WikipediaPageState {
  entityType: string; // "character", "location", etc.
  categoryName: string | null;
  pageNumber: number;
  pageSize: number;
  entities: Entity[];
  totalCount: number;
  loading: boolean;
}

// Load entities with pagination
const loadEntities = async (state: WikipediaPageState) => {
  const params = new URLSearchParams({
    entityType: state.entityType,
    pageNumber: state.pageNumber.toString(),
    pageSize: state.pageSize.toString()
  });

  if (state.categoryName) {
    params.append('categoryName', state.categoryName);
  }

  const response = await fetch(
    `/api/novels/${novelId}/entities?${params}`
  );

  return response.json();
};

// Infinite scroll implementation
const loadMore = async (state: WikipediaPageState) => {
  if (state.loading) return;

  setState({ ...state, loading: true });

  const data = await loadEntities({
    ...state,
    pageNumber: state.pageNumber + 1
  });

  setState({
    entities: [...state.entities, ...data.items],
    totalCount: data.totalCount,
    pageNumber: data.currentPage,
    loading: false
  });
};

// Load all at once for small categories (max 100)
const loadAllEntities = async (entityType: string, categoryName: string) => {
  const response = await fetch(
    `/api/novels/${novelId}/entities?entityType=${entityType}&categoryName=${categoryName}&pageSize=100`
  );
  return response.json();
};
```

---

### **Workflow 4: Image Management**

```typescript
// Option A: Direct URL approach
const createEntityWithImage = async (entity: EntityData, imageFile: File) => {
  // 1. Upload image to your CDN/Cloudflare first
  const imageUrl = await uploadToCloudflare(imageFile);

  // 2. Create entity with imageUrl
  return await fetch(`/api/novels/${novelId}/entities`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({
      ...entity,
      imageUrl // ? Direct URL
    })
  });
};

// Option B: Gallery approach
const createEntityWithGallery = async (entity: EntityData, imageFile: File) => {
  // 1. Create entity first
  const response = await fetch(`/api/novels/${novelId}/entities`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(entity)
  });

  const { entityId } = await response.json();

  // 2. Upload image through gallery
  const formData = new FormData();
  formData.append('imageFile', imageFile);
  formData.append('caption', 'Profile image');
  formData.append('orderIndex', '0');

  return await fetch(`/api/novels/${novelId}/entities/${entityId}/gallery`, {
    method: 'POST',
    body: formData
  });
};

// Hybrid approach (recommended)
const createEntityHybrid = async (
  entity: EntityData,
  mainImage: File,
  galleryImages: File[]
) => {
  // 1. Upload main image separately (faster)
  const mainImageUrl = await uploadToCloudflare(mainImage);

  // 2. Create entity with main image URL
  const response = await fetch(`/api/novels/${novelId}/entities`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({
      ...entity,
      imageUrl: mainImageUrl // ? Main profile image
    })
  });

  const { entityId } = await response.json();

  // 3. Upload additional gallery images
  await Promise.all(
    galleryImages.map((file, index) => {
      const formData = new FormData();
      formData.append('imageFile', file);
      formData.append('orderIndex', index.toString());

      return fetch(`/api/novels/${novelId}/entities/${entityId}/gallery`, {
        method: 'POST',
        body: formData
      });
    })
  );
};
```

---

## ?? **Updated Response Models**

### **PaginatedEntityListResponse**
```typescript
{
  items: EntityListItem[]
  totalCount: number
  pageSize: number
  currentPage: number
  totalPages: number
}
```

### **EntityListItem** (List view - lightweight)
```typescript
{
  id: string
  entityType: string
  categoryName?: string
  name: string
  shortDescription?: string // ? Use for cards
  imageUrl?: string
  createdAt: string
  articlesCount: number
  relationshipsCount: number
}
```

### **EntityDTO** (Detail view - complete)
```typescript
{
  id: string
  novelId: string
  entityType: string
  categoryName?: string
  name: string
  shortDescription?: string
  description?: string
  role?: string
  imageUrl?: string
  attributes: object
  articles: EntityArticleDTO[] // ? Can be updated/deleted now
  galleryImages: EntityGalleryImageDTO[]
  relationships: EntityRelationshipDTO[] // ? Can be updated/deleted now
  createdAt: string
  updatedAt: string
}
```

---

## ?? **Best Practices**

### **1. Pagination Strategy**

**Small Categories (<50 items):**
```typescript
// Load all at once
pageSize = 100
```

**Large Categories (>50 items):**
```typescript
// Use pagination
pageSize = 20
// Load more on scroll
```

### **2. Image Upload Strategy**

**Main Profile Image:**
- Use `imageUrl` field (faster)
- Upload separately to Cloudflare
- Update entity with URL

**Additional Images:**
- Use gallery system
- Upload through gallery endpoint
- Automatic ordering

### **3. Article Management**

**Editing:**
- Only send changed fields in PATCH
- Frontend handles optimistic updates
- Revert on error

**Reordering:**
- Update `orderIndex` for each article
- Do in single batch operation
- Show loading state

### **4. Relationship Management**

**Creating:**
- Validate both entities exist first
- Show relationship preview before creating
- Allow inline editing of label/description

**Deleting:**
- Show confirmation dialog
- Explain impact (both entities affected)
- Offer "archive" vs "delete" option

---

## ? **Complete Feature Matrix**

| Feature | Status | Endpoints | Notes |
|---------|--------|-----------|-------|
| **Entity CRUD** | ? Complete | POST, PATCH, DELETE, GET | Fully functional |
| **List & Filter** | ? Complete | GET with params | Pagination supported |
| **Search** | ? Complete | GET /search | Elasticsearch |
| **Categories** | ? Complete | GET /categories | Dynamic categories |
| **Articles - Add** | ? Complete | POST /articles | ? |
| **Articles - Update** | ? **NEW** | PATCH /articles/{id} | **Just added** |
| **Articles - Delete** | ? **NEW** | DELETE /articles/{id} | **Just added** |
| **Gallery - Upload** | ? Complete | POST /gallery | Cloudflare R2 |
| **Gallery - Delete** | ? Complete | DELETE /gallery/{id} | ? |
| **Relationships - Create** | ? Complete | POST /relationships | ? |
| **Relationships - Update** | ? **NEW** | PATCH /relationships/{id} | **Just added** |
| **Relationships - Delete** | ? **NEW** | DELETE /relationships/{id} | **Just added** |

---

## ?? **Summary**

**All frontend questions answered:**
1. ? **Article UPDATE/DELETE** - Fully implemented
2. ? **Relationship management** - UPDATE and DELETE added
3. ? **Pagination** - Already supported, choose pageSize based on category
4. ? **Image upload** - Two options: direct URL (recommended for main) or gallery system

**System is now 100% feature-complete for Wikipedia-style entity management!** ??

---

**Last Updated:** January 10, 2025
**API Version:** 1.1.0 (Added PATCH/DELETE for articles and relationships)
