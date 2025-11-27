using Domain.Entities;
using Domain.Repositories;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Repositories;

public class NovelEntityRepository(ApplicationDbContext dbContext) : INovelEntityRepository
{
    public async Task<NovelEntity> CreateEntityAsync(NovelEntity entity)
    {
        dbContext.NovelEntities.Add(entity);
        await dbContext.SaveChangesAsync();
        return entity;
    }

    public async Task<NovelEntity?> GetEntityByIdAsync(Guid entityId)
    {
        return await dbContext.NovelEntities
            .AsNoTracking()
            .Include(e => e.Novel)
            .Include(e => e.Articles.Where(a => !a.IsDeleted))
            .Include(e => e.GalleryImages)
            .Include(e => e.SourceRelationships.Where(r => !r.IsDeleted))
                .ThenInclude(r => r.TargetEntity)
            .Include(e => e.TargetRelationships.Where(r => !r.IsDeleted))
                .ThenInclude(r => r.SourceEntity)
            .FirstOrDefaultAsync(e => e.Id == entityId);
    }

    public async Task<bool> UpdateEntityAsync(NovelEntity entity)
    {
        entity.UpdatedAt = DateTime.UtcNow;
        dbContext.NovelEntities.Update(entity);
        return await dbContext.SaveChangesAsync() > 0;
    }

    public async Task<bool> DeleteEntityAsync(Guid entityId)
    {
        var entity = await dbContext.NovelEntities.FindAsync(entityId);
        if (entity == null) return false;
        
        entity.MarkAsDeleted();
        return await dbContext.SaveChangesAsync() > 0;
    }

    public async Task<(IEnumerable<NovelEntity>, int)> GetNovelEntitiesAsync(
        Guid novelId,
        string? section = null,
        int pageNumber = 1,
        int pageSize = 20)
    {
        var query = dbContext.NovelEntities
            .AsNoTracking()
            .Where(e => e.NovelId == novelId && !e.IsDeleted && !e.Name.StartsWith("_section_"))
            .Include(e => e.Articles)
            .Include(e => e.SourceRelationships)
            .Include(e => e.TargetRelationships)
            .AsQueryable();

        if (!string.IsNullOrEmpty(section))
        {
            query = query.Where(e => e.Section == section);
        }

        var totalCount = await query.CountAsync();

        var entities = await query
            .OrderBy(e => e.CreatedAt)
            .Skip((pageNumber - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        return (entities, totalCount);
    }

    public async Task<List<string>> GetSectionsForNovelAsync(Guid novelId)
    {
        return await dbContext.NovelEntities
            .Where(e => e.NovelId == novelId && !e.IsDeleted && !e.Name.StartsWith("_section_"))
            .OrderBy(e => e.CreatedAt)
            .Select(e => e.Section)
            .Distinct()
            .ToListAsync();
    }

    public async Task<List<string>> GetAllSectionsIncludingEmptyAsync(Guid novelId)
    {
        return await dbContext.NovelEntities
            .Where(e => e.NovelId == novelId && !e.IsDeleted)
            .OrderBy(e => e.CreatedAt)
            .Select(e => e.Section)
            .Distinct()
            .ToListAsync();
    }

    public async Task<List<(Guid? Id, string Section, string? Icon)>> GetSectionsWithIconsAsync(Guid novelId)
    {
        return await dbContext.NovelEntities
            .Where(e => e.NovelId == novelId && !e.IsDeleted)
            .GroupBy(e => e.Section)
            .Select(g => new 
            { 
                Id = g.OrderBy(e => e.CreatedAt).First().Id,
                Section = g.Key, 
                Icon = g.OrderBy(e => e.CreatedAt).First().Icon,
                FirstCreated = g.Min(e => e.CreatedAt)
            })
            .OrderBy(x => x.FirstCreated)
            .Select(x => ValueTuple.Create((Guid?)x.Id, x.Section, x.Icon))
            .ToListAsync();
    }

    public async Task<Dictionary<string, string?>> GetSectionIconsAsync(Guid novelId)
    {
        return await dbContext.NovelEntities
            .Where(e => e.NovelId == novelId && !e.IsDeleted)
            .GroupBy(e => e.Section)
            .Select(g => new 
            { 
                Section = g.Key, 
                Icon = g.OrderBy(e => e.CreatedAt).First().Icon 
            })
            .ToDictionaryAsync(x => x.Section, x => x.Icon);
    }

    // Article operations
    public async Task<EntityArticle> AddArticleAsync(EntityArticle article)
    {
        await dbContext.EntityArticles.AddAsync(article);
        await dbContext.SaveChangesAsync();
        return article;
    }

    public async Task<EntityArticle?> GetArticleByIdAsync(Guid articleId)
    {
        return await dbContext.EntityArticles
            .Include(a => a.Entity)
            .FirstOrDefaultAsync(a => a.Id == articleId);
    }

    public async Task UpdateArticleAsync(EntityArticle article)
    {
        dbContext.EntityArticles.Update(article);
        await dbContext.SaveChangesAsync();
    }

    public async Task<bool> DeleteArticleAsync(Guid articleId)
    {
        var article = await dbContext.EntityArticles.FindAsync(articleId);
        if (article == null) return false;

        article.IsDeleted = true;
        await dbContext.SaveChangesAsync();
        return true;
    }

    public async Task<bool> ReorderArticlesAsync(Guid entityId, List<Guid> orderedArticleIds)
    {
        var articles = await dbContext.EntityArticles
            .Where(a => a.EntityId == entityId && !a.IsDeleted)
            .ToListAsync();

        var existingArticleIds = articles.Select(a => a.Id).ToHashSet();
        var providedArticleIds = orderedArticleIds.ToHashSet();

        // Validate all article IDs belong to this entity
        if (!existingArticleIds.SetEquals(providedArticleIds))
        {
            return false;
        }

        // Update order indices
        for (int i = 0; i < orderedArticleIds.Count; i++)
        {
            var article = articles.First(a => a.Id == orderedArticleIds[i]);
            article.OrderIndex = i;
        }

        // Save in transaction
        using var transaction = await dbContext.Database.BeginTransactionAsync();
        try
        {
            await dbContext.SaveChangesAsync();
            await transaction.CommitAsync();
            return true;
        }
        catch
        {
            await transaction.RollbackAsync();
            return false;
        }
    }

    // Relationship operations
    public async Task<EntityRelationship> CreateRelationshipAsync(EntityRelationship relationship)
    {
        await dbContext.EntityRelationships.AddAsync(relationship);
        await dbContext.SaveChangesAsync();
        return relationship;
    }

    public async Task<EntityRelationship?> GetRelationshipByIdAsync(Guid relationshipId)
    {
        return await dbContext.EntityRelationships
            .Include(r => r.SourceEntity)
            .Include(r => r.TargetEntity)
            .FirstOrDefaultAsync(r => r.Id == relationshipId);
    }

    public async Task UpdateRelationshipAsync(EntityRelationship relationship)
    {
        dbContext.EntityRelationships.Update(relationship);
        await dbContext.SaveChangesAsync();
    }

    public async Task<bool> DeleteRelationshipAsync(Guid relationshipId)
    {
        var relationship = await dbContext.EntityRelationships.FindAsync(relationshipId);
        if (relationship == null) return false;

        relationship.MarkAsDeleted();
        await dbContext.SaveChangesAsync();
        return true;
    }

    public async Task<List<EntityRelationship>> GetEntityRelationshipsAsync(Guid entityId)
    {
        return await dbContext.EntityRelationships
            .AsNoTracking()
            .Include(r => r.TargetEntity)
            .Where(r => r.SourceEntityId == entityId)
            .OrderBy(r => r.CreatedAt)
            .ToListAsync();
    }

    public async Task<EntityGalleryImage> AddGalleryImageAsync(EntityGalleryImage image)
    {
        await dbContext.EntityGalleryImages.AddAsync(image);
        await dbContext.SaveChangesAsync();
        return image;
    }

    public async Task<EntityGalleryImage?> GetGalleryImageByIdAsync(Guid imageId)
    {
        return await dbContext.EntityGalleryImages
            .AsNoTracking()
            .FirstOrDefaultAsync(i => i.Id == imageId);
    }

    public async Task<bool> UpdateGalleryImageAsync(EntityGalleryImage image)
    {
        dbContext.EntityGalleryImages.Update(image);
        return await dbContext.SaveChangesAsync() > 0;
    }

    public async Task<bool> DeleteGalleryImageAsync(Guid imageId)
    {
        var image = await dbContext.EntityGalleryImages.FindAsync(imageId);
        if (image == null) return false;

        dbContext.EntityGalleryImages.Remove(image);
        await dbContext.SaveChangesAsync();
        return true;
    }

    public async Task<List<EntityGalleryImage>> GetEntityGalleryImagesAsync(Guid entityId)
    {
        return await dbContext.EntityGalleryImages
            .AsNoTracking()
            .Where(i => i.EntityId == entityId)
            .OrderBy(i => i.OrderIndex)
            .ToListAsync();
    }

    public async Task<bool> UpdateGalleryImageOrderAsync(Guid imageId, int newOrderIndex)
    {
        var image = await dbContext.EntityGalleryImages.FindAsync(imageId);
        if (image == null) return false;

        image.OrderIndex = newOrderIndex;
        await dbContext.SaveChangesAsync();
        return true;
    }

    public async Task<bool> ReorderGalleryImagesAsync(Guid entityId, List<Guid> orderedImageIds)
    {
        var images = await dbContext.EntityGalleryImages
            .Where(i => i.EntityId == entityId)
            .ToListAsync();

        var existingImageIds = images.Select(i => i.Id).ToHashSet();
        var providedImageIds = orderedImageIds.ToHashSet();

        if (!existingImageIds.SetEquals(providedImageIds))
        {
            return false;
        }

        for (int i = 0; i < orderedImageIds.Count; i++)
        {
            var image = images.First(img => img.Id == orderedImageIds[i]);
            image.OrderIndex = i;
        }

        using var transaction = await dbContext.Database.BeginTransactionAsync();
        try
        {
            await dbContext.SaveChangesAsync();
            await transaction.CommitAsync();
            return true;
        }
        catch
        {
            await transaction.RollbackAsync();
            return false;
        }
    }
}
