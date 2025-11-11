# Entity System Simplification - Refactor Plan

## ?? Goal
Simplify the entity system from confusing EntityType/CategoryName to simple Section-based structure.

## ?? Current vs New Structure

### **Current (Confusing):**
```
EntityType: "character"  ? What is this?
CategoryName: "Main Heroes"  ? Is this a sub-category?
Icon: "users"  ? Where does this go?
```

### **New (Simple):**
```
Section: "??????" (Characters)  ? Clear grouping
Icon: "users"  ? Icon for the section
Name: "????????"  ? The actual entity
```

---

## ? Changes Completed

### 1. Domain Model
- ? Renamed `EntityType` ? `Section`
- ? Removed `CategoryName`
- ? Kept `Icon` for section visualization

### 2. Database Configuration
- ? Updated ApplicationDbContext
- ? **Migration pending** (need to fix all code first)

### 3. DTOs
- ? Updated `EntityDTO`
- ? Updated `EntityListDTO`
- ? Updated `EntityTypeStatsDTO` ? uses `Section`
- ? Renamed `EntityTypeSummaryDTO` ? `SectionSummaryDTO`

### 4. Commands
- ? Updated `CreateEntityCommand`
- ? Updated `UpdateEntityCommand`
- ? Updated handlers for both

### 5. Validators
- ? Updated `CreateEntityCommandValidator`
- ? Updated `UpdateEntityCommandValidator`

### 6. Queries
- ? Updated `GetNovelEntitiesQuery`
- ? Updated `GetNovelEntitiesQueryHandler`
- ? Updated `GetEntityByIdQueryHandler`

### 7. Repository
- ? Updated `INovelEntityRepository` interface
- ? Updated `NovelEntityRepository` implementation
- ? Methods now use `section` parameter

---

## ? Remaining Work

### 8. Search System
- ? Update `SearchEntitiesQuery`
- ? Update `SearchEntitiesQueryHandler`
- ? Update `NovelEntitySearchDocument`
- ? Update `EntitySearchService`

### 9. GetSections Query
- ? Rename `GetEntityTypesQuery` ? `GetSectionsQuery`
- ? Rename `GetEntityTypesQueryHandler` ? `GetSectionsQueryHandler`
- ? Update handler logic

### 10. Controller
- ? Update `EntityController` endpoints
- ? Update parameter names: `entityType` ? `section`
- ? Update route: `/categories` ? `/sections`

### 11. Migration
- ? Create migration: `SimplifyEntityToSectionBased`
- ? Apply migration to database

---

## ?? New API Design

### **List Sections:**
```
GET /api/novels/{novelId}/entities/sections
Response:
{
  "sections": [
    { "name": "??????", "icon": "users", "count": 5 },
    { "name": "?????", "icon": "map-pin", "count": 3 }
  ],
  "totalCount": 8
}
```

### **List Entities in Section:**
```
GET /api/novels/{novelId}/entities?section=??????&pageNumber=1&pageSize=20
Response:
{
  "items": [
    {
      "id": "guid",
      "section": "??????",
      "icon": "users",
      "name": "????????",
      "shortDescription": "???? ????",
      "imageUrl": "...",
      "articlesCount": 2,
      "relationshipsCount": 3
    }
  ],
  "totalPages": 1,
  "totalItemsCount": 5
}
```

### **Create Entity:**
```
POST /api/novels/{novelId}/entities
Content-Type: multipart/form-data

FormData:
  section: "??????"
  icon: "users"
  name: "????????"
  shortDescription: "???? ????"
  imageFile: <file>
```

### **Search Entities:**
```
GET /api/novels/{novelId}/entities/search?query=????????&section=??????
```

---

## ??? Database Migration

```sql
-- Rename column
EXEC sp_rename 'NovelEntities.EntityType', 'Section', 'COLUMN';

-- Drop CategoryName column
ALTER TABLE NovelEntities DROP COLUMN CategoryName;

-- Update index if exists
-- (Entity Framework will handle this automatically)
```

---

## ?? Benefits

### Before:
```
Novel ? EntityType ? CategoryName ? Entity
         (character) ? (Main Heroes) ? (Aragorn)
```
**Confusing!** Two levels of categorization

### After:
```
Novel ? Section ? Entity
        (??????) ? (???????)
```
**Simple!** One clear grouping

---

## ?? Breaking Changes

### Frontend Must Update:
1. API calls: `entityType` ? `section`
2. Response fields: `entityType` ? `section`
3. Remove all references to `categoryName`
4. Endpoint: `/categories` ? `/sections`

### Database:
1. Column renamed: `EntityType` ? `Section`
2. Column removed: `CategoryName`
3. Elasticsearch index needs reindexing

---

## ?? Next Steps

**Option A: Complete Refactor Now**
- Finish remaining files (Search, Controller)
- Create and apply migration
- Update documentation
- **Time:** ~30 minutes

**Option B: Rollback and Plan Better**
- Revert all changes
- Create feature branch
- Test thoroughly before merging
- **Time:** ~10 minutes rollback + later implementation

**Which do you prefer?** ??

---

**Status:** 70% Complete (7/11 sections done)
**Estimated Remaining Time:** 20-30 minutes
**Risk Level:** Medium (breaking changes for frontend)
