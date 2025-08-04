using Application.Common;
using Application.Novels.DTOS;
using MediatR;

namespace Application.Novels.Queries.GetPopularByGenre;

public class GetNovelsInGenreQuery(string slug, int pageSize, int pageNumber,string sorting, bool isCompleted) : IRequest<PagedResult<NovelInRankingDto>>
{
    public string Slug { get; set; } = slug;
    public int PageSize { get; set; } = pageSize;
    public int PageNumber { get; set; } = pageNumber;
    public string Sorting { get; set; } = sorting;
    public bool IsCompleted { get; set; } = isCompleted;
}
