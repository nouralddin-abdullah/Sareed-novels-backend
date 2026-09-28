using Application.Gifts.DTOs;
using AutoMapper;
using Domain.Repositories;
using MediatR;
using Application.Common;

namespace Application.Gifts.Queries.GetGlobalLeaderboard;

public class GetGlobalLeaderboardQueryHandler(
    IGlobalSupporterLeaderboardRepository leaderboardRepository,
    IMapper mapper) : IRequestHandler<GetGlobalLeaderboardQuery, GlobalLeaderboardDto>
{
    public async Task<GlobalLeaderboardDto> Handle(GetGlobalLeaderboardQuery request, CancellationToken cancellationToken)
    {
        var (pageNumber, pageSize) = Paging.Clamp(request.PageNumber, request.PageSize, GiftPaging.MaxPageSize);
        var (supporters, totalCount) = await leaderboardRepository.GetLeaderboard(request.Period, pageNumber, pageSize);

        var supporterDtos = mapper.Map<List<TopSupporterDto>>(supporters);

        return new GlobalLeaderboardDto
        {
            Supporters = supporterDtos,
            TotalCount = totalCount,
            LastUpdated = supporters.FirstOrDefault()?.LastUpdated ?? DateTime.UtcNow
        };
    }
}
