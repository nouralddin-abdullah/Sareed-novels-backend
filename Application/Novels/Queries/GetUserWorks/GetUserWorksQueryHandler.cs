using Application.Common;
using Application.Novels.DTOS;
using AutoMapper;
using Domain.Repositories;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Application.Novels.Queries.GetUserWorks;

public class GetUserWorksQueryHandler(
    ILogger<GetUserWorksQueryHandler> logger,
    INovelsRepository novelsRepository,
    IMapper mapper) : IRequestHandler<GetUserWorksQuery, PagedResult<MyWorksDTO>>
{
    public async Task<PagedResult<MyWorksDTO>> Handle(GetUserWorksQuery request, CancellationToken cancellationToken)
    {
        logger.LogInformation("Getting works for user {UserId}", request.UserId);
        
        var (novels, totalCount) = await novelsRepository.GetUserPublishedWorks(
            request.UserId, 
            request.PageNumber, 
            request.PageSize);
        
        var novelsDto = mapper.Map<IEnumerable<MyWorksDTO>>(novels);
        
        return new PagedResult<MyWorksDTO>(novelsDto, totalCount, request.PageSize, request.PageNumber);
    }
}
