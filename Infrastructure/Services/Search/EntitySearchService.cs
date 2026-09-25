using Application.Common;
using Application.Entities.DTOs;
using Application.Services;
using Domain.Search;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Services.Search;

/// <summary>
/// Novel wiki search straight from SQL. Query words must appear in NovelEntity.SearchText (name + descriptions);
/// name matches rank first. The "_section_" rows that only hold an empty section's name and icon are not entities
/// and are never returned (same rule as the wiki listing in NovelEntityRepository).
/// </summary>
public class EntitySearchService(ApplicationDbContext dbContext) : IEntitySearchService
{
    private const string SectionPlaceholderPrefix = "_section_";

    public async Task<PagedResult<EntityListDTO>> SearchEntitiesAsync(
        Guid novelId,
        string? query = null,
        string? section = null,
        int pageNumber = 1,
        int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        pageNumber = Math.Clamp(pageNumber, 1, NovelSearchService.MaxPageNumber);
        pageSize = Math.Clamp(pageSize, 1, NovelSearchService.MaxPageSize);
        var tokens = SearchText.Tokens(query);
        if (SearchText.HasNothingSearchable(query, tokens))
        {
            return new PagedResult<EntityListDTO>([], 0, pageSize, pageNumber);
        }

        var phrase = string.Join(' ', tokens);

        var entities = dbContext.NovelEntities.AsNoTracking()
            .Where(e => e.NovelId == novelId && !e.Name.StartsWith(SectionPlaceholderPrefix));

        if (!string.IsNullOrWhiteSpace(section))
        {
            entities = entities.Where(e => e.Section == section);
        }

        foreach (var token in tokens)
        {
            entities = entities.Where(e => e.SearchText.Contains(token));
        }

        var totalCount = await entities.CountAsync(cancellationToken);

        var ordered = tokens.Count == 0
            ? entities.OrderBy(e => e.Name)
            : entities
                .OrderBy(e => e.SearchName == phrase ? 0
                    : e.SearchName.StartsWith(phrase) ? 1
                    : e.SearchName.Contains(phrase) ? 2
                    : 3)
                .ThenBy(e => e.Name);

        var items = await ordered
            .ThenBy(e => e.Id)
            .Skip((pageNumber - 1) * pageSize)
            .Take(pageSize)
            .Select(e => new EntityListDTO
            {
                Id = e.Id,
                Section = e.Section,
                Icon = e.Icon,
                Name = e.Name,
                ShortDescription = e.ShortDescription,
                ImageUrl = e.ImageUrl,
                CreatedAt = e.CreatedAt,
                ArticlesCount = e.Articles.Count,
                RelationshipsCount = e.SourceRelationships.Count + e.TargetRelationships.Count
            })
            .ToListAsync(cancellationToken);

        return new PagedResult<EntityListDTO>(items, totalCount, pageSize, pageNumber);
    }
}
