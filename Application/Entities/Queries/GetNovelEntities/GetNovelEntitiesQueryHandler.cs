using Application.Common;
using Application.Entities.DTOs;
using Domain.Repositories;
using Domain.Seo;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Application.Entities.Queries.GetNovelEntities;

public class GetNovelEntitiesQueryHandler(
    ILogger<GetNovelEntitiesQueryHandler> logger,
    INovelEntityRepository entityRepository) : IRequestHandler<GetNovelEntitiesQuery, PagedResult<EntityListDTO>>
{
    /// <summary>The SEO worker lists up to 100 wiki entries of a novel in one request.</summary>
    public const int MaxPageSize = 100;

    public async Task<PagedResult<EntityListDTO>> Handle(GetNovelEntitiesQuery request, CancellationToken cancellationToken)
    {
        logger.LogInformation("Getting entities for novel {NovelId}", request.NovelId);

        var (pageNumber, pageSize) = Paging.Clamp(request.PageNumber, request.PageSize, MaxPageSize);
        var (entities, totalCount) = await entityRepository.GetNovelEntitiesAsync(
            request.NovelId,
            request.Section,
            pageNumber,
            pageSize);

        var dtos = entities.Select(e => new EntityListDTO
        {
            Id = e.Id,
            Section = e.Section,
            Icon = e.Icon,
            Name = e.Name,
            ShortDescription = e.ShortDescription,
            ImageUrl = e.ImageUrl,
            CreatedAt = e.CreatedAt,
            ArticlesCount = e.Articles.Count,
            RelationshipsCount = e.SourceRelationships.Count + e.TargetRelationships.Count,
            IsIndexable = WikiPages.IsIndexable(e)
        }).ToList();

        return new PagedResult<EntityListDTO>(dtos, totalCount, pageSize, pageNumber);
    }
}
