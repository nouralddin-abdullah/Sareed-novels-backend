using Domain.Entities;

namespace Domain.Repositories;

public interface INovelEntityRepository
{
    // Entity CRUD
    Task<NovelEntity> CreateEntityAsync(NovelEntity entity);
    Task<NovelEntity?> GetEntityByIdAsync(Guid entityId);
    Task<bool> UpdateEntityAsync(NovelEntity entity);
    Task<bool> DeleteEntityAsync(Guid entityId); // Soft delete
    
    // Entity queries
    Task<(IEnumerable<NovelEntity>, int)> GetNovelEntitiesAsync(
        Guid novelId,
        string? section = null,
        int pageNumber = 1,
        int pageSize = 20);
    
    Task<List<string>> GetSectionsForNovelAsync(Guid novelId);
    Task<List<string>> GetAllSectionsIncludingEmptyAsync(Guid novelId); // NEW: Gets all sections even if they only have placeholders
    Task<List<(string Section, string? Icon)>> GetSectionsWithIconsAsync(Guid novelId); // NEW: Gets sections with their icons in one query
    Task<Dictionary<string, string?>> GetSectionIconsAsync(Guid novelId); // NEW: Gets icons for each section (including from placeholders)
    
    // Article operations
    Task<EntityArticle> AddArticleAsync(EntityArticle article);
    Task<EntityArticle?> GetArticleByIdAsync(Guid articleId);
    Task UpdateArticleAsync(EntityArticle article);
    Task<bool> DeleteArticleAsync(Guid articleId);
    Task<bool> ReorderArticlesAsync(Guid entityId, List<Guid> orderedArticleIds);
    
    // Relationship operations
    Task<EntityRelationship> CreateRelationshipAsync(EntityRelationship relationship);
    Task<EntityRelationship?> GetRelationshipByIdAsync(Guid relationshipId);
    Task UpdateRelationshipAsync(EntityRelationship relationship);
    Task<bool> DeleteRelationshipAsync(Guid relationshipId);
    Task<List<EntityRelationship>> GetEntityRelationshipsAsync(Guid entityId);
    
    // Gallery operations
    Task<EntityGalleryImage> AddGalleryImageAsync(EntityGalleryImage image);
    Task<EntityGalleryImage?> GetGalleryImageByIdAsync(Guid imageId);
    Task<bool> UpdateGalleryImageAsync(EntityGalleryImage image);
    Task<bool> DeleteGalleryImageAsync(Guid imageId);
    Task<List<EntityGalleryImage>> GetEntityGalleryImagesAsync(Guid entityId);
    Task<bool> UpdateGalleryImageOrderAsync(Guid imageId, int newOrderIndex);
    Task<bool> ReorderGalleryImagesAsync(Guid entityId, List<Guid> orderedImageIds);
}
