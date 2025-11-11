using Application.Common;
using Application.Entities.DTOs;
using Domain.Repositories;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Application.Entities.Queries.GetNovelEntities;

public class GetNovelEntitiesQueryHandler(
    ILogger<GetNovelEntitiesQueryHandler> logger,
    INovelEntityRepository entityRepository) : IRequestHandler<GetNovelEntitiesQuery, PagedResult<EntityListDTO>>
{
    public async Task<PagedResult<EntityListDTO>> Handle(GetNovelEntitiesQuery request, CancellationToken cancellationToken)
    {
        logger.LogInformation("Getting entities for novel {NovelId}", request.NovelId);

        var (entities, totalCount) = await entityRepository.GetNovelEntitiesAsync(
            request.NovelId,
            request.Section,
            request.PageNumber,
            request.PageSize);

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
            RelationshipsCount = e.SourceRelationships.Count + e.TargetRelationships.Count
        }).ToList();

        return new PagedResult<EntityListDTO>(dtos, totalCount, request.PageSize, request.PageNumber);
    }
}
