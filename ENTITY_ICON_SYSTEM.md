# Category Icon System - Complete Documentation

## ?? Overview

The entity system now supports **category icons** that the frontend can use to visually represent different entity types and categories. Icons are based on **lucide-react** icon library.

---

## ?? **Available Icons**

The system supports **8 predefined icons** that map to common entity categories:

| Icon ID | Arabic Label | Use Case | Component |
|---------|--------------|----------|-----------|
| `users` | ?????? | Characters | Users |
| `map-pin` | ????? | Locations | MapPin |
| `shield` | ???? / ????? | Shields/Items | Shield |
| `flag` | ????? | Factions/Organizations | Flag |
| `book` | ??? | Books/Lore | Book |
| `sparkles` | ??? | Magic Systems | Sparkles |
| `swords` | ????? | Weapons | Swords |
| `globe` | ????? | Worlds/Realms | Globe |

---

## ?? **API Changes**

### **1. Create Entity (Updated)**

**Endpoint:** `POST /api/novels/{novelId}/entities`

**Request Body:**
```json
{
  "entityType": "character",
  "categoryName": "Main Heroes",
  "icon": "users",  // ? NEW: Optional icon identifier
  "name": "Aragorn",
  "shortDescription": "The reluctant hero",
  "role": "Protagonist",
  "imageUrl": "https://...",
  "attributes": {...}
}
```

**Field Description:**

| Field | Type | Required | Max Length | Valid Values | Description |
|-------|------|----------|------------|--------------|-------------|
| `icon` | string | ? | 20 | See table above | Icon identifier for category visualization |

**Validation:**
- Icon is optional (can be `null`)
- If provided, must be one of the 8 valid icons
- Case-insensitive matching
- Alternative format `mappin` accepted (converted to `map-pin`)
- Invalid icons return `400 Bad Request` with list of valid icons

**Example Requests:**

```json
// Valid - Using icon
POST /api/novels/{novelId}/entities
{
  "entityType": "character",
  "categoryName": "??????? ?????????",
  "icon": "users",
  "name": "???????",
  "shortDescription": "????? ???????"
}

// Valid - No icon
POST /api/novels/{novelId}/entities
{
  "entityType": "location",
  "categoryName": "?????",
  "name": "????? ???????"
}

// Invalid - Bad icon
POST /api/novels/{novelId}/entities
{
  "entityType": "weapon",
  "icon": "invalid-icon",  // ? Error: Invalid icon
  "name": "Excalibur"
}
```

**Error Response:**
```json
{
  "success": false,
  "message": "Invalid icon. Valid icons are: users, map-pin, shield, flag, book, sparkles, swords, globe"
}
```

---

### **2. Update Entity (Updated)**

**Endpoint:** `PATCH /api/novels/{novelId}/entities/{entityId}`

**Request Body:**
```json
{
  "categoryName": "Legendary Heroes",
  "icon": "swords"  // ? NEW: Can update icon
}
```

**Same validation as Create Entity**

---

### **3. Get Entity (Updated Response)**

**Endpoint:** `GET /api/novels/{novelId}/entities/{entityId}`

**Response:**
```json
{
  "id": "guid",
  "novelId": "guid",
  "entityType": "character",
  "categoryName": "Main Heroes",
  "icon": "users",  // ? NEW: Icon in response
  "name": "Aragorn",
  "shortDescription": "The reluctant hero",
  "description": "...",
  "role": "Protagonist",
  "imageUrl": "https://...",
  "attributes": {...},
  "articles": [...],
  "galleryImages": [...],
  "relationships": [...],
  "createdAt": "2024-01-01T00:00:00Z",
  "updatedAt": "2024-01-15T00:00:00Z"
}
```

---

### **4. List Entities (Updated Response)**

**Endpoint:** `GET /api/novels/{novelId}/entities`

**Response:**
```json
{
  "items": [
    {
      "id": "guid",
      "entityType": "character",
      "categoryName": "Main Heroes",
      "icon": "users",  // ? NEW: Icon in list view
      "name": "Aragorn",
      "shortDescription": "The reluctant hero",
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

---

### **5. Search Entities (Updated)**

**Endpoint:** `GET /api/novels/{novelId}/entities/search`

Icons are indexed in Elasticsearch and included in search results.

---

## ?? **Icon Validation Rules**

### **Backend Validation:**

```csharp
// Valid icons (case-insensitive)
var validIcons = new[] 
{
    "users",
    "map-pin",
    "mappin",     // Alternative format (converted to "map-pin")
    "shield",
    "flag",
    "book",
    "sparkles",
    "swords",
    "globe"
};
```

### **Normalization:**
- Converts to lowercase
- Replaces underscores with hyphens (`map_pin` ? `map-pin`)
- Validates against allowed list
- Returns `null` for invalid icons

---

## ?? **Frontend Integration**

### **Icon Component Mapping:**

```typescript
import { Users, MapPin, Shield, Flag, Book, Sparkles, Swords, Globe } from 'lucide-react';

export const ICON_COMPONENTS = {
  'users': Users,
  'map-pin': MapPin,
  'mappin': MapPin,     // Alternative
  'shield': Shield,
  'flag': Flag,
  'book': Book,
  'sparkles': Sparkles,
  'swords': Swords,
  'globe': Globe,
};

// Usage
const IconComponent = ICON_COMPONENTS[entity.icon] || Users;
return <IconComponent className="w-5 h-5" />;
```

---

### **Category Creation Flow:**

```typescript
interface CategoryForm {
  entityType: string;
  categoryName: string;
  icon: string;  // Selected from dropdown
}

// 1. Show icon selector
const IconSelector = () => {
  const icons = [
    { id: 'users', label: '??????', component: Users },
    { id: 'map-pin', label: '?????', component: MapPin },
    { id: 'shield', label: '???? / ?????', component: Shield },
    { id: 'flag', label: '?????', component: Flag },
    { id: 'book', label: '???', component: Book },
    { id: 'sparkles', label: '???', component: Sparkles },
    { id: 'swords', label: '?????', component: Swords },
    { id: 'globe', label: '?????', component: Globe },
  ];

  return (
    <div className="icon-grid">
      {icons.map(({ id, label, component: Icon }) => (
        <button key={id} onClick={() => selectIcon(id)}>
          <Icon className="w-6 h-6" />
          <span>{label}</span>
        </button>
      ))}
    </div>
  );
};

// 2. Create entity with icon
const createEntity = async (data: CategoryForm) => {
  const response = await fetch(`/api/novels/${novelId}/entities`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({
      entityType: data.entityType,
      categoryName: data.categoryName,
      icon: data.icon,  // ? Icon ID
      name: data.name,
      // ... other fields
    })
  });

  if (!response.ok) {
    const error = await response.json();
    // Handle: "Invalid icon. Valid icons are: ..."
  }
};
```

---

### **Display Entity with Icon:**

```typescript
interface EntityCardProps {
  entity: Entity;
}

const EntityCard = ({ entity }: EntityCardProps) => {
  const IconComponent = ICON_COMPONENTS[entity.icon || 'users'];
  
  return (
    <div className="entity-card">
      <div className="icon-container">
        <IconComponent className="w-8 h-8 text-blue-500" />
      </div>
      <h3>{entity.name}</h3>
      <p className="text-sm text-gray-600">{entity.categoryName}</p>
      <p className="text-sm">{entity.shortDescription}</p>
    </div>
  );
};
```

---

### **Category List with Icons:**

```typescript
const CategoryList = ({ categories }: { categories: Category[] }) => {
  return (
    <div className="category-list">
      {categories.map(category => {
        const IconComponent = ICON_COMPONENTS[category.icon || 'users'];
        
        return (
          <div key={category.name} className="category-item">
            <IconComponent className="w-5 h-5" />
            <span>{category.name}</span>
            <span className="count">{category.entityCount}</span>
          </div>
        );
      })}
    </div>
  );
};
```

---

## ?? **Use Cases**

### **1. Entity Type Categories with Icons:**

```json
// Characters - users icon
{
  "entityType": "character",
  "categoryName": "??????? ?????????",
  "icon": "users",
  "name": "???????"
}

// Locations - map-pin icon
{
  "entityType": "location",
  "categoryName": "????? ??????",
  "icon": "map-pin",
  "name": "????? ??????"
}

// Weapons - swords icon
{
  "entityType": "weapon",
  "categoryName": "??????? ?????????",
  "icon": "swords",
  "name": "???????"
}

// Magic Systems - sparkles icon
{
  "entityType": "magic-system",
  "categoryName": "????? ???????",
  "icon": "sparkles",
  "name": "?????? ??????"
}

// Organizations - flag icon
{
  "entityType": "organization",
  "categoryName": "???????",
  "icon": "flag",
  "name": "????? ??????"
}
```

---

## ?? **Updated Response Models**

### **EntityDTO (Complete)**

```typescript
interface EntityDTO {
  id: string;
  novelId: string;
  entityType: string;
  categoryName?: string;
  icon?: string;  // ? NEW: "users" | "map-pin" | "shield" | etc.
  name: string;
  shortDescription?: string;
  description?: string;
  role?: string;
  imageUrl?: string;
  attributes: Record<string, any>;
  articles: EntityArticleDTO[];
  galleryImages: EntityGalleryImageDTO[];
  relationships: EntityRelationshipDTO[];
  createdAt: string;
  updatedAt: string;
}
```

### **EntityListItem (List View)**

```typescript
interface EntityListItem {
  id: string;
  entityType: string;
  categoryName?: string;
  icon?: string;  // ? NEW
  name: string;
  shortDescription?: string;
  imageUrl?: string;
  createdAt: string;
  articlesCount: number;
  relationshipsCount: number;
}
```

---

## ? **Validation Summary**

| Scenario | Result | HTTP Status |
|----------|--------|-------------|
| Valid icon (`"users"`) | Accepted | 200 OK |
| Valid icon uppercase (`"USERS"`) | Normalized to `"users"` | 200 OK |
| Alternative format (`"mappin"`) | Normalized to `"map-pin"` | 200 OK |
| No icon (`null`) | Accepted (optional) | 200 OK |
| Invalid icon (`"invalid"`) | Rejected | 400 Bad Request |
| Empty string (`""`) | Treated as `null` | 200 OK |

---

## ?? **Database Schema**

### **NovelEntity Table:**

```sql
ALTER TABLE NovelEntities
ADD Icon NVARCHAR(20) NULL;

-- Index (optional, for filtering by icon)
CREATE INDEX IX_NovelEntities_Icon 
ON NovelEntities(Icon) 
WHERE Icon IS NOT NULL;
```

---

## ?? **Migration Steps**

### **Apply Migration:**

```bash
dotnet ef database update
```

### **Existing Entities:**
- Icon field will be `NULL` for existing entities
- Frontend should use default icon (`users`) when `icon` is `null`
- Authors can update entities to add icons

---

## ?? **Best Practices**

### **1. Icon Selection:**
- Match icon to entity type (characters ? users, locations ? map-pin)
- Use consistent icons for similar categories across novels
- Provide icon selector in category creation UI

### **2. Default Behavior:**
- If icon is `null`, use `"users"` as default
- Show icon selection as optional
- Allow icon update without changing other fields

### **3. UI Design:**
- Display icon next to category name in lists
- Use icon in entity cards/badges
- Show icon in category filters/tabs

### **4. Validation:**
- Validate icon on frontend before submission
- Handle backend validation errors gracefully
- Show list of valid icons in error message

---

## ?? **Example UI Patterns**

### **Category Filter with Icons:**

```tsx
<div className="category-filters">
  {categories.map(cat => {
    const Icon = ICON_COMPONENTS[cat.icon] || Users;
    return (
      <button 
        key={cat.name}
        className={selected === cat.name ? 'active' : ''}
        onClick={() => setSelected(cat.name)}
      >
        <Icon className="w-4 h-4" />
        <span>{cat.name}</span>
        <Badge>{cat.count}</Badge>
      </button>
    );
  })}
</div>
```

### **Entity Type Tabs with Icons:**

```tsx
<Tabs>
  <Tab value="character">
    <Users className="w-5 h-5" />
    <span>????????</span>
  </Tab>
  <Tab value="location">
    <MapPin className="w-5 h-5" />
    <span>???????</span>
  </Tab>
  <Tab value="weapon">
    <Swords className="w-5 h-5" />
    <span>???????</span>
  </Tab>
</Tabs>
```

---

## ?? **Complete Feature Matrix**

| Feature | Status | Notes |
|---------|--------|-------|
| Icon field in entity | ? Complete | Max 20 chars |
| Icon validation | ? Complete | 8 valid icons |
| Icon normalization | ? Complete | Lowercase, hyphen conversion |
| CREATE with icon | ? Complete | Optional field |
| UPDATE icon | ? Complete | Partial update supported |
| GET returns icon | ? Complete | In all responses |
| Elasticsearch indexed | ? Complete | Searchable |
| Database migration | ? Complete | `AddIconToEntity` |

---

## ?? **Summary**

**Icon system is fully integrated:**
1. ? **8 predefined icons** based on lucide-react
2. ? **Validation** - Only valid icons accepted
3. ? **Normalization** - Case-insensitive, format flexible
4. ? **Optional** - Entities can have no icon
5. ? **Searchable** - Indexed in Elasticsearch
6. ? **Updatable** - Can change icon without affecting other fields
7. ? **Backward compatible** - Existing entities work with `null` icon

**Frontend can now:**
- Show visual icons for categories
- Let users pick icons when creating categories
- Display icons in entity lists and cards
- Use icons in filters and navigation

---

**Last Updated:** January 11, 2025
**API Version:** 1.2.0 (Added icon support)
