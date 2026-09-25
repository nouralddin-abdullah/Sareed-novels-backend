using Application.Common;
using Domain.Repositories;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Application.Novels.Queries.GetAllNovels;

public class GetAllNovelsQueryHandler(
    ILogger<GetAllNovelsQueryHandler> logger,
    INovelsRepository novelsRepository) : IRequestHandler<GetAllNovelsQuery, PagedResult<NovelBasicDTO>>
{
    // The SEO worker's legacy sitemap fallback asks for 1000 at once.
    public const int MaxPageSize = 1000;

    public async Task<PagedResult<NovelBasicDTO>> Handle(GetAllNovelsQuery request, CancellationToken cancellationToken)
    {
        // pageNumber 0 used to reach SQL as a negative OFFSET (500); pageSize 0 reported int.MaxValue pages.
        var pageNumber = Math.Max(1, request.PageNumber);
        var pageSize = Math.Clamp(request.PageSize, 1, MaxPageSize);
        logger.LogInformation("Getting all novels page {Page} size {Size}", pageNumber, pageSize);

        var (novels, totalCount) = await novelsRepository.GetAllNovelsBasicAsync(pageNumber, pageSize);

        var items = novels.Select(n => new NovelBasicDTO
        {
            Id = n.Id,
            Slug = n.Slug,
            Title = n.Title,
            CreatedAt = n.CreatedAt,
            UpdatedAt = n.LastUpdatedAt
        }).ToList();

        return new PagedResult<NovelBasicDTO>(items, totalCount, pageSize, pageNumber);
    }
}
