using Application.Common;
using Application.Novels.DTOS;
using MediatR;

namespace Application.Rankings.Queries.GetSiteWideRanking;

public class GetSiteWideRankingQuery(string rankingType, int pageSize, int pageNumber) : IRequest<PagedResult<NovelInRankingDto>>
{
    public string RankingType { get; set; } = rankingType; // "AllTime", "Trending", "NewArrivals"
    public int PageSize { get; set; } = pageSize;
    public int PageNumber { get; set; } = pageNumber;
}
