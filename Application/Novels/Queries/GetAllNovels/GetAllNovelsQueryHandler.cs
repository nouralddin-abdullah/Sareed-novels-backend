using Application.Common;
using Domain.Repositories;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Application.Novels.Queries.GetAllNovels;

public class GetAllNovelsQueryHandler(
    ILogger<GetAllNovelsQueryHandler> logger,
    INovelsRepository novelsRepository) : IRequestHandler<GetAllNovelsQuery, PagedResult<NovelBasicDTO>>
{
    public async Task<PagedResult<NovelBasicDTO>> Handle(GetAllNovelsQuery request, CancellationToken cancellationToken)
    {
        logger.LogInformation("Getting all novels page {Page} size {Size}", request.PageNumber, request.PageSize);

        var (novels, totalCount) = await novelsRepository.GetAllNovelsBasicAsync(request.PageNumber, request.PageSize);

        var items = novels.Select(n => new NovelBasicDTO
        {
            Id = n.Id,
            Slug = n.Slug,
            Title = n.Title,
            CreatedAt = n.CreatedAt,
            UpdatedAt = n.LastUpdatedAt
        }).ToList();

        return new PagedResult<NovelBasicDTO>(items, totalCount, request.PageSize, request.PageNumber);
    }
}
