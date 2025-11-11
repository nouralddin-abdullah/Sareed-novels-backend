# Novel Entity System - API Documentation

## ?? Overview

The Novel Entity System is a flexible, user-driven knowledge base that allows novel authors to document **any type of entity** in their fictional universe. Think of it as a customizable Wikipedia for your novel.

**Supported Entity Types (User-Defined):**
- Characters
- Locations
- Magic Systems
- Organizations
- Items/Artifacts
- Abilities/Powers
- Creatures
- Timeline Events
- Or any custom type you define!

---

## ?? Key Features

- **? Flexible Schema:** Define any entity type with custom attributes
- **? Category System:** Group entities within types (e.g., "Main Characters", "Minor NPCs")
- **? Rich Profiles:** Short descriptions, roles, detailed descriptions, and custom attributes
- **? Image Galleries:** Upload multiple images per entity with captions
- **? Articles/Lore:** Add multiple articles (backstories, histories, etc.)
- **? Relationships:** Connect entities with typed relationships
- **? Full-Text Search:** Elasticsearch-powered search across all entity data
- **? Arabic Support:** Optimized for Arabic content

---

## ?? Base URL

```
/api/novels/{novelId}/entities
```

All endpoints require `{novelId}` in the route to scope entities to a specific novel.

---

## ?? Endpoints

### **1. Create Entity**

Create a new entity in the novel's knowledge base.

**Endpoint:** `POST /api/novels/{novelId}/entities`

**Authorization:** Required (Novel owner only)

**Request Body:**
```json
{
  "entityType": "string (required, max 50 chars)",
  "categoryName": "string (optional, max 100 chars)",
  "name": "string (required, max 200 chars)",
  "shortDescription": "string (optional, max 500 chars)",
  "description": "string (optional, max 5000 chars)",
  "role": "string (optional, max 100 chars)",
  "imageUrl": "string (optional, max 500 chars)",
  "attributes": {
    // Flexible JSON object - any structure
    "key1": "value1",
    "key2": 123,
    "key3": ["array", "values"],
    "nested": { "object": true }
  }
}
```

**Field Descriptions:**

| Field | Type | Required | Max Length | Description |
|-------|------|----------|------------|-------------|
| `entityType` | string | ? | 50 | Type of entity (user-defined): "character", "location", "magic-system", etc. |
| `categoryName` | string | ? | 100 | Optional grouping within type: "Main Characters", "Legendary Items" |
| `name` | string | ? | 200 | Entity name |
| `shortDescription` | string | ? | 500 | Brief description for cards/previews (Arabic-optimized) |
| `description` | string | ? | 5000 | Full detailed description |
| `role` | string | ? | 100 | Entity role/title: "Main Protagonist", "Antagonist" |
| `imageUrl` | string | ? | 500 | Main profile image URL |
| `attributes` | object | ? | - | Flexible JSON object for custom attributes |

**Response:** `200 OK`
```json
{
  "success": true,
  "message": "Entity created successfully"
}
```

**Errors:**
- `400 Bad Request` - Validation errors
- `401 Unauthorized` - Not authenticated
- `403 Forbidden` - Not novel owner
- `404 Not Found` - Novel not found

**Example Request:**
```json
POST /api/novels/123e4567-e89b-12d3-a456-426614174000/entities

{
  "entityType": "character",
  "categoryName": "Main Heroes",
  "name": "Aragorn",
  "shortDescription": "The reluctant hero with a mysterious past",
  "description": "Aragorn is the heir of Isildur and the rightful King of Gondor...",
  "role": "Main Protagonist",
  "imageUrl": "https://cdn.example.com/aragorn.jpg",
  "attributes": {
    "age": 87,
    "race": "Human (Dúnedain)",
    "title": "King of Gondor",
    "weapon": "Andúril",
    "height": "6'6\"",
    "skills": ["Swordsmanship", "Leadership", "Tracking"],
    "alignment": "Lawful Good"
  }
}
```

---

### **2. Update Entity**

Update an existing entity (partial update supported).

**Endpoint:** `PATCH /api/novels/{novelId}/entities/{entityId}`

**Authorization:** Required (Novel owner only)

**Request Body:**
```json
{
  "name": "string (optional, max 200 chars)",
  "shortDescription": "string (optional, max 500 chars)",
  "description": "string (optional, max 5000 chars)",
  "role": "string (optional, max 100 chars)",
  "imageUrl": "string (optional, max 500 chars)",
  "categoryName": "string (optional, max 100 chars)",
  "attributes": {
    // Will replace entire attributes object if provided
  }
}
```

**Note:** Only include fields you want to update. `entityType` cannot be changed.

**Response:** `200 OK`
```json
{
  "success": true,
  "message": "Entity updated successfully"
}
```

**Errors:**
- `400 Bad Request` - Validation errors
- `401 Unauthorized` - Not authenticated
- `403 Forbidden` - Not novel owner
- `404 Not Found` - Entity or novel not found

**Example Request:**
```json
PATCH /api/novels/123e4567/entities/789abcde

{
  "shortDescription": "Updated description",
  "role": "King and Protector",
  "attributes": {
    "age": 88,
    "title": "High King of Gondor and Arnor"
  }
}
```

---

### **3. Delete Entity**

Soft delete an entity (sets `IsDeleted = true`).

**Endpoint:** `DELETE /api/novels/{novelId}/entities/{entityId}`

**Authorization:** Required (Novel owner only)

**Response:** `204 No Content`

**Errors:**
- `401 Unauthorized` - Not authenticated
- `403 Forbidden` - Not novel owner
- `404 Not Found` - Entity or novel not found

---

### **4. Get Novel Entities (List)**

Retrieve paginated list of entities for a novel with optional filtering.

**Endpoint:** `GET /api/novels/{novelId}/entities`

**Authorization:** Optional (Public data)

**Query Parameters:**

| Parameter | Type | Required | Default | Description |
|-----------|------|----------|---------|-------------|
| `entityType` | string | ? | - | Filter by entity type: "character", "location", etc. |
| `categoryName` | string | ? | - | Filter by category name |
| `pageNumber` | integer | ? | 1 | Page number (1-indexed) |
| `pageSize` | integer | ? | 20 | Items per page (max 100) |

**Response:** `200 OK`
```json
{
  "items": [
    {
      "id": "guid",
      "entityType": "character",
      "categoryName": "Main Heroes",
      "name": "Aragorn",
      "imageUrl": "https://...",
      "createdAt": "2024-01-01T00:00:00Z",
      "articlesCount": 3,
      "relationshipsCount": 5
    }
  ],
  "totalCount": 42,
  "pageSize": 20,
  "currentPage": 1,
  "totalPages": 3
}
```

**Example Requests:**
```
GET /api/novels/{novelId}/entities
GET /api/novels/{novelId}/entities?entityType=character
GET /api/novels/{novelId}/entities?entityType=character&categoryName=Main Heroes
GET /api/novels/{novelId}/entities?pageNumber=2&pageSize=50
```

---

### **5. Get Entity by ID**

Retrieve complete entity details including articles, gallery, and relationships.

**Endpoint:** `GET /api/novels/{novelId}/entities/{entityId}`

**Authorization:** Optional (Public data)

**Response:** `200 OK`
```json
{
  "id": "guid",
  "novelId": "guid",
  "entityType": "character",
  "categoryName": "Main Heroes",
  "name": "Aragorn",
  "shortDescription": "The reluctant hero with a mysterious past",
  "description": "Full detailed description...",
  "role": "Main Protagonist",
  "imageUrl": "https://main-profile.jpg",
  "attributes": {
    "age": 87,
    "race": "Human",
    "title": "King of Gondor",
    "weapon": "Andúril",
    "skills": ["Swordsmanship", "Leadership"]
  },
  "articles": [
    {
      "id": "guid",
      "title": "Early Life",
      "content": "Born in Rivendell...",
      "orderIndex": 0,
      "createdAt": "2024-01-01T00:00:00Z"
    },
    {
      "id": "guid",
      "title": "The Quest",
      "content": "Journey begins...",
      "orderIndex": 1,
      "createdAt": "2024-01-02T00:00:00Z"
    }
  ],
  "galleryImages": [
    {
      "id": "guid",
      "imageUrl": "https://gallery-1.jpg",
      "caption": "Battle scene",
      "orderIndex": 0,
      "createdAt": "2024-01-01T00:00:00Z"
    },
    {
      "id": "guid",
      "imageUrl": "https://gallery-2.jpg",
      "caption": "Portrait",
      "orderIndex": 1,
      "createdAt": "2024-01-02T00:00:00Z"
    }
  ],
  "relationships": [
    {
      "id": "guid",
      "targetEntityId": "guid",
      "targetEntityName": "Arwen",
      "targetEntityImage": "https://arwen.jpg",
      "relationType": "romantic",
      "label": "Loves",
      "description": "Star-crossed lovers"
    },
    {
      "id": "guid",
      "targetEntityId": "guid",
      "targetEntityName": "Gandalf",
      "targetEntityImage": "https://gandalf.jpg",
      "relationType": "ally",
      "label": "Trusted Mentor",
      "description": "Guided by the wizard"
    }
  ],
  "createdAt": "2024-01-01T00:00:00Z",
  "updatedAt": "2024-01-15T00:00:00Z"
}
```

**Errors:**
- `404 Not Found` - Entity not found

---

### **6. Search Entities (Elasticsearch)**

Full-text search across all entity data with advanced filtering.

**Endpoint:** `GET /api/novels/{novelId}/entities/search`

**Authorization:** Optional (Public data)

**Query Parameters:**

| Parameter | Type | Required | Default | Description |
|-----------|------|----------|---------|-------------|
| `query` | string | ? | - | Search query (searches name, description, attributes, articles) |
| `entityType` | string | ? | - | Filter by entity type |
| `pageNumber` | integer | ? | 1 | Page number |
| `pageSize` | integer | ? | 20 | Items per page (max 100) |

**Response:** `200 OK`
```json
{
  "items": [
    {
      "id": "guid",
      "novelId": "guid",
      "entityType": "character",
      "categoryName": "Main Heroes",
      "name": "Aragorn",
      "shortDescription": "The reluctant hero...",
      "description": "Full description...",
      "role": "Main Protagonist",
      "imageUrl": "https://...",
      "attributes": {...},
      "articles": [...],
      "relationships": [...],
      "tags": [],
      "createdAt": "2024-01-01T00:00:00Z",
      "updatedAt": "2024-01-15T00:00:00Z"
    }
  ],
  "totalCount": 15,
  "pageSize": 20,
  "currentPage": 1,
  "totalPages": 1
}
```

**Search Features:**
- ? Full-text search across name, description, articles
- ? Dynamic attribute matching
- ? Fuzzy matching for typos
- ? Arabic text optimized
- ? Results sorted by relevance

**Example Requests:**
```
GET /api/novels/{novelId}/entities/search?query=fire
GET /api/novels/{novelId}/entities/search?query=aragorn
GET /api/novels/{novelId}/entities/search?entityType=character&query=hero
GET /api/novels/{novelId}/entities/search?query=sword&pageSize=50
```

---

### **7. Get Categories**

Get list of all unique category names for a specific entity type.

**Endpoint:** `GET /api/novels/{novelId}/entities/categories`

**Authorization:** Optional (Public data)

**Query Parameters:**

| Parameter | Type | Required | Default | Description |
|-----------|------|----------|---------|-------------|
| `entityType` | string | ? | - | Filter categories by entity type |

**Response:** `200 OK`
```json
{
  "entityType": "character",
  "categories": [
    "Main Heroes",
    "Villains",
    "Side Characters",
    "NPCs"
  ],
  "count": 4
}
```

**Example Requests:**
```
GET /api/novels/{novelId}/entities/categories
GET /api/novels/{novelId}/entities/categories?entityType=character
GET /api/novels/{novelId}/entities/categories?entityType=location
```

---

## ?? Articles System

### **8. Add Article to Entity**

Add a backstory, history, or lore article to an entity.

**Endpoint:** `POST /api/novels/{novelId}/entities/{entityId}/articles`

**Authorization:** Required (Novel owner only)

**Request Body:**
```json
{
  "title": "string (required, max 200 chars)",
  "content": "string (required)",
  "orderIndex": "integer (required)"
}
```

**Field Descriptions:**

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| `title` | string | ? | Article title (e.g., "Early Life", "The Quest") |
| `content` | string | ? | Article content (supports markdown) |
| `orderIndex` | integer | ? | Display order (0-based) |

**Response:** `200 OK`
```json
{
  "success": true,
  "message": "Article added successfully"
}
```

**Errors:**
- `400 Bad Request` - Validation errors
- `401 Unauthorized` - Not authenticated
- `403 Forbidden` - Not novel owner
- `404 Not Found` - Entity not found

**Example Request:**
```json
POST /api/novels/{novelId}/entities/{entityId}/articles

{
  "title": "Early Life",
  "content": "Born in Rivendell under the protection of Elrond...",
  "orderIndex": 0
}
```

---

## ??? Gallery System

### **9. Upload Gallery Image**

Upload an image to the entity's gallery (Cloudflare R2).

**Endpoint:** `POST /api/novels/{novelId}/entities/{entityId}/gallery`

**Authorization:** Required (Novel owner only)

**Request:** `multipart/form-data`

**Form Fields:**

| Field | Type | Required | Max Size | Description |
|-------|------|----------|----------|-------------|
| `imageFile` | file | ? | 10MB | Image file (JPEG, PNG, WebP) |
| `caption` | string | ? | 500 chars | Image caption/description |
| `orderIndex` | integer | ? | - | Display order (default: 0) |

**Response:** `200 OK`
```json
{
  "success": true,
  "message": "Gallery image added successfully"
}
```

**Errors:**
- `400 Bad Request` - Invalid file or validation errors
- `401 Unauthorized` - Not authenticated
- `403 Forbidden` - Not novel owner
- `404 Not Found` - Entity not found
- `413 Payload Too Large` - File exceeds 10MB

**Storage Details:**
- Files stored in Cloudflare R2
- Path: `entity-gallery/{entityId}/{guid}`
- Returns CDN URL for fast delivery
- Supports JPEG, PNG, WebP formats

**Example Request (cURL):**
```bash
curl -X POST \
  https://api.example.com/api/novels/{novelId}/entities/{entityId}/gallery \
  -H "Authorization: Bearer {token}" \
  -F "imageFile=@battle-scene.jpg" \
  -F "caption=Epic battle scene" \
  -F "orderIndex=0"
```

---

### **10. Remove Gallery Image**

Delete an image from the entity's gallery.

**Endpoint:** `DELETE /api/novels/{novelId}/entities/gallery/{imageId}`

**Authorization:** Required (Novel owner only)

**Response:** `204 No Content`

**Errors:**
- `401 Unauthorized` - Not authenticated
- `403 Forbidden` - Not novel owner
- `404 Not Found` - Image not found

---

## ?? Relationships System

### **11. Create Relationship**

Create a typed relationship between two entities.

**Endpoint:** `POST /api/novels/{novelId}/entities/relationships`

**Authorization:** Required (Novel owner only)

**Request Body:**
```json
{
  "sourceEntityId": "guid (required)",
  "targetEntityId": "guid (required)",
  "relationType": "string (required, max 50 chars)",
  "label": "string (optional, max 100 chars)",
  "description": "string (optional, max 1000 chars)"
}
```

**Field Descriptions:**

| Field | Type | Required | Max Length | Description |
|-------|------|----------|------------|-------------|
| `sourceEntityId` | guid | ? | - | Source entity ID (relationship origin) |
| `targetEntityId` | guid | ? | - | Target entity ID (relationship destination) |
| `relationType` | string | ? | 50 | Relationship type (user-defined): "ally", "enemy", "family", "romantic", "mentor" |
| `label` | string | ? | 100 | Relationship label: "Best Friend", "Father of", "Mentor to" |
| `description` | string | ? | 1000 | Detailed relationship description |

**Response:** `200 OK`
```json
{
  "success": true,
  "message": "Relationship created successfully"
}
```

**Errors:**
- `400 Bad Request` - Validation errors or entities must be in same novel
- `401 Unauthorized` - Not authenticated
- `403 Forbidden` - Not novel owner
- `404 Not Found` - Source or target entity not found

**Example Request:**
```json
POST /api/novels/{novelId}/entities/relationships

{
  "sourceEntityId": "aragorn-guid",
  "targetEntityId": "arwen-guid",
  "relationType": "romantic",
  "label": "Loves",
  "description": "Star-crossed lovers whose union bridges the worlds of Men and Elves"
}
```

**Common Relationship Types:**
- `ally` - Allied forces
- `enemy` - Adversaries
- `family` - Family relations
- `romantic` - Romantic relationships
- `mentor` - Teacher/student
- `rival` - Competitors
- `created-by` - Creator relationship
- `member-of` - Organization membership
- `located-in` - Location hierarchy

---

## ?? Advanced Features

### **Flexible Attributes System**

Attributes can store any JSON structure:

**Simple Attributes:**
```json
{
  "age": 87,
  "race": "Human",
  "power": "Fire Magic"
}
```

**Complex Nested Attributes:**
```json
{
  "stats": {
    "strength": 85,
    "intelligence": 92,
    "agility": 78
  },
  "equipment": [
    {
      "name": "Andúril",
      "type": "Sword",
      "enchantment": "Flame of the West"
    }
  ],
  "abilities": {
    "combat": ["Swordsmanship", "Leadership"],
    "magic": ["Healing", "Protection"]
  }
}
```

**Searchable:** All attributes are indexed in Elasticsearch and searchable.

---

### **Entity Type Examples**

**Characters:**
```json
{
  "entityType": "character",
  "categoryName": "Main Heroes",
  "name": "Aragorn",
  "role": "Protagonist",
  "attributes": {
    "age": 87,
    "race": "Human",
    "class": "Ranger/King"
  }
}
```

**Locations:**
```json
{
  "entityType": "location",
  "categoryName": "Cities",
  "name": "Minas Tirith",
  "role": "Capital City",
  "attributes": {
    "population": 100000,
    "region": "Gondor",
    "climate": "Temperate"
  }
}
```

**Magic Systems:**
```json
{
  "entityType": "magic-system",
  "categoryName": "Elemental Magic",
  "name": "Firebending",
  "role": "Combat Magic",
  "attributes": {
    "element": "Fire",
    "difficulty": "Advanced",
    "chakra": "Solar Plexus"
  }
}
```

**Organizations:**
```json
{
  "entityType": "organization",
  "categoryName": "Guilds",
  "name": "Thieves Guild",
  "role": "Criminal Organization",
  "attributes": {
    "members": 500,
    "influence": "High",
    "headquarters": "Riften"
  }
}
```

**Items/Artifacts:**
```json
{
  "entityType": "item",
  "categoryName": "Legendary Weapons",
  "name": "Excalibur",
  "role": "Sword of Kings",
  "attributes": {
    "type": "Sword",
    "rarity": "Legendary",
    "power": "Divine Light"
  }
}
```

---

## ?? Response Models

### **EntityListDTO**
```typescript
{
  id: string (guid)
  entityType: string
  categoryName?: string
  name: string
  imageUrl?: string
  createdAt: string (ISO 8601)
  articlesCount: number
  relationshipsCount: number
}
```

### **EntityDTO**
```typescript
{
  id: string (guid)
  novelId: string (guid)
  entityType: string
  categoryName?: string
  name: string
  shortDescription?: string
  description?: string
  role?: string
  imageUrl?: string
  attributes: object
  articles: EntityArticleDTO[]
  galleryImages: EntityGalleryImageDTO[]
  relationships: EntityRelationshipDTO[]
  createdAt: string (ISO 8601)
  updatedAt: string (ISO 8601)
}
```

### **EntityArticleDTO**
```typescript
{
  id: string (guid)
  title: string
  content: string
  orderIndex: number
  createdAt: string (ISO 8601)
}
```

### **EntityGalleryImageDTO**
```typescript
{
  id: string (guid)
  imageUrl: string
  caption?: string
  orderIndex: number
  createdAt: string (ISO 8601)
}
```

### **EntityRelationshipDTO**
```typescript
{
  id: string (guid)
  targetEntityId: string (guid)
  targetEntityName: string
  targetEntityImage?: string
  relationType: string
  label?: string
  description?: string
}
```

---

## ?? Authorization

### **Public Endpoints (No Auth Required):**
- `GET /api/novels/{novelId}/entities` - List entities
- `GET /api/novels/{novelId}/entities/{entityId}` - Get entity details
- `GET /api/novels/{novelId}/entities/search` - Search entities
- `GET /api/novels/{novelId}/entities/categories` - Get categories

### **Protected Endpoints (Novel Owner Only):**
- `POST /api/novels/{novelId}/entities` - Create entity
- `PATCH /api/novels/{novelId}/entities/{entityId}` - Update entity
- `DELETE /api/novels/{novelId}/entities/{entityId}` - Delete entity
- `POST /api/novels/{novelId}/entities/{entityId}/articles` - Add article
- `POST /api/novels/{novelId}/entities/{entityId}/gallery` - Upload image
- `DELETE /api/novels/{novelId}/entities/gallery/{imageId}` - Remove image
- `POST /api/novels/{novelId}/entities/relationships` - Create relationship

**Authentication:** Bearer token in `Authorization` header

---

## ? Performance & Limits

### **Rate Limits:**
- Standard endpoints: 100 requests/minute
- Search endpoint: 60 requests/minute
- Upload endpoint: 20 requests/minute

### **Size Limits:**
- Request body: 1MB (except file uploads)
- Gallery image: 10MB per file
- Attributes JSON: 100KB
- Article content: Unlimited (but consider readability)

### **Pagination:**
- Default page size: 20 items
- Max page size: 100 items
- Page numbers: 1-indexed

### **Search Performance:**
- Average response time: 20-50ms
- Supports 1000+ entities per novel
- Real-time indexing via outbox pattern

---

## ?? Internationalization

### **Arabic Support:**
- ? All text fields support Arabic
- ? Elasticsearch uses Arabic analyzer
- ? Right-to-left (RTL) rendering supported
- ? Fuzzy matching optimized for Arabic
- ? Diacritic handling

**Example Arabic Entity:**
```json
{
  "entityType": "?????",
  "categoryName": "??????? ?????????",
  "name": "???????",
  "shortDescription": "????? ??????? ????? ????? ??????? ?? ?????? ?????? ???????",
  "role": "????? ???????",
  "attributes": {
    "?????": 87,
    "?????": "?????",
    "?????": "??????? ???????"
  }
}
```

---

## ?? Data Sync & Consistency

### **Elasticsearch Synchronization:**

The system uses an **outbox pattern** for reliable Elasticsearch indexing:

1. **Create/Update Entity** ? SQL transaction + outbox entry
2. **Background Service** (every 30 seconds) ? Process outbox
3. **Index in Elasticsearch** ? Mark outbox as processed

**Typical sync time:** 30-60 seconds

**Check sync status via SearchIndexOutbox table** (admin access required)

---

## ?? Use Cases

### **1. Character Wiki**
Create detailed character profiles with:
- Short bio for quick reference
- Full backstory articles
- Character gallery (concept art, scenes)
- Relationships with other characters

### **2. World Building**
Document your fictional world:
- Locations with maps and descriptions
- Magic systems with rules and examples
- Organizations and their hierarchies
- Historical events and timelines

### **3. Equipment & Items**
Catalog items in your novel:
- Legendary weapons with lore
- Artifacts with powers
- Regular items with significance
- Gallery showing different forms/states

### **4. Creature Compendium**
Create a bestiary:
- Creature types and variants
- Abilities and weaknesses
- Habitats and behaviors
- Multiple images showing features

---

## ?? Error Codes

| Status | Code | Message | Description |
|--------|------|---------|-------------|
| 400 | `BadRequest` | Validation failed | Check request body |
| 401 | `Unauthorized` | Not authenticated | Provide valid token |
| 403 | `Forbidden` | Permission denied | Must be novel owner |
| 404 | `NotFound` | Resource not found | Entity/Novel doesn't exist |
| 413 | `PayloadTooLarge` | File too large | Max 10MB for images |
| 429 | `TooManyRequests` | Rate limit exceeded | Wait before retrying |
| 500 | `InternalServerError` | Server error | Contact support |

**Error Response Format:**
```json
{
  "success": false,
  "message": "Detailed error message",
  "errors": {
    "fieldName": ["Validation error details"]
  }
}
```

---

## ?? Examples

### **Complete Workflow Example**

```bash
# 1. Create a character entity
POST /api/novels/{novelId}/entities
{
  "entityType": "character",
  "categoryName": "Main Heroes",
  "name": "Aragorn",
  "shortDescription": "The reluctant king",
  "role": "Protagonist",
  "imageUrl": "https://cdn/main-profile.jpg",
  "attributes": {
    "age": 87,
    "race": "Human"
  }
}

# 2. Add backstory articles
POST /api/novels/{novelId}/entities/{entityId}/articles
{
  "title": "Early Life",
  "content": "Born in Rivendell...",
  "orderIndex": 0
}

POST /api/novels/{novelId}/entities/{entityId}/articles
{
  "title": "The Quest",
  "content": "Journey to destroy the ring...",
  "orderIndex": 1
}

# 3. Upload gallery images
POST /api/novels/{novelId}/entities/{entityId}/gallery
FormData: imageFile=battle-scene.jpg, caption="Battle", orderIndex=0

POST /api/novels/{novelId}/entities/{entityId}/gallery
FormData: imageFile=portrait.jpg, caption="Portrait", orderIndex=1

# 4. Create relationships
POST /api/novels/{novelId}/entities/relationships
{
  "sourceEntityId": "{aragorn-id}",
  "targetEntityId": "{arwen-id}",
  "relationType": "romantic",
  "label": "Loves"
}

# 5. Retrieve complete entity
GET /api/novels/{novelId}/entities/{entityId}

# 6. Search for entities
GET /api/novels/{novelId}/entities/search?query=aragorn
```

---

## ?? Best Practices

### **1. Entity Naming**
- Use consistent entity type names across your novel
- Keep entity types lowercase: "character", "location"
- Use descriptive category names: "Main Heroes" not "Group 1"

### **2. Attributes Structure**
- Keep attributes consistent within entity types
- Use nested objects for complex data
- Avoid deeply nested structures (max 3 levels)

### **3. Articles Organization**
- Use `orderIndex` to control reading order
- Keep articles focused on specific topics
- Use descriptive titles

### **4. Gallery Management**
- Upload images in order of importance
- Use meaningful captions
- Optimize images before upload (< 2MB recommended)

### **5. Relationships**
- Use consistent relationship type names
- Add descriptive labels for clarity
- Create bidirectional relationships when needed

### **6. Search Optimization**
- Include important keywords in descriptions
- Use short descriptions for better previews
- Tag entities with relevant attributes

---

## ?? Support & Resources

**Need Help?**
- API Status: `https://status.example.com`
- Developer Discord: `https://discord.gg/example`
- Issue Tracker: `https://github.com/example/issues`

**Admin Endpoints:**
- Initialize Elasticsearch: `POST /api/admin/elasticsearch/init`
- Reindex Entities: `POST /api/admin/elasticsearch/reindex-entities/{novelId}`

---

## ?? Changelog

### **Version 1.0.0** (Current)
- ? Initial release
- ? Full CRUD operations
- ? Gallery system with Cloudflare R2
- ? Articles/Lore system
- ? Relationships system
- ? Elasticsearch full-text search
- ? Arabic language optimization
- ? Category system

---

**End of Documentation**

*Last Updated: January 10, 2025*
*API Version: 1.0.0*
