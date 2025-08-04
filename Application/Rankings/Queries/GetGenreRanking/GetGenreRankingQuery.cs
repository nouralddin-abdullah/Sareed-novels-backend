using Application.Common;
using Application.Novels.DTOS;
using MediatR;

namespace Application.Rankings.Queries.GetGenreRanking;

public class GetGenreRankingQuery(string genreSlug, string rankingType, int pageSize, int pageNumber) : IRequest<PagedResult<NovelInRankingDto>>
{
    public string GenreSlug { get; set; } = genreSlug;
    public string RankingType { get; set; } = rankingType; // "TopRated", "Trending", "New"
    public int PageSize { get; set; } = pageSize;
    public int PageNumber { get; set; } = pageNumber;
}
