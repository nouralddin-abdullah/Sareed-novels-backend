using Application.Common;
using Application.Novels.DTOS;
using AutoMapper;
using Domain.Repositories;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Application.Novels.Queries.GetUserWorks;

/// <summary>
/// GET /api/myworks/user/{userId}: a member's public works, the same for every caller. With
/// <see cref="GetUserWorksQuery.WithChapters"/> (#46) only the novels a reader can open, with at least one published
/// chapter, and the paging counts only those; without it every public novel, with or without one. Drafts and deleted
/// novels are never listed (the author has GET /api/myworks).
/// </summary>
public class GetUserWorksQueryHandler(
    ILogger<GetUserWorksQueryHandler> logger,
    INovelsRepository novelsRepository,
    IMapper mapper) : IRequestHandler<GetUserWorksQuery, PagedResult<MyWorksDTO>>
{
    public async Task<PagedResult<MyWorksDTO>> Handle(GetUserWorksQuery request, CancellationToken cancellationToken)
    {
        logger.LogInformation("Getting works for user {UserId}", request.UserId);

        var (pageNumber, pageSize) = Paging.Clamp(request.PageNumber, request.PageSize);
        var (novels, totalCount) = await novelsRepository.GetUserPublishedWorks(
            request.UserId,
            pageNumber,
            pageSize,
            readableOnly: request.WithChapters);

        var novelsDto = mapper.Map<IEnumerable<MyWorksDTO>>(novels);

        return new PagedResult<MyWorksDTO>(novelsDto, totalCount, pageSize, pageNumber);
    }
}
