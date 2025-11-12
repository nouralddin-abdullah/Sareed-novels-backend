using Application.Common;
using MediatR;

namespace Application.Novels.Queries.GetAllNovels;

public class GetAllNovelsQuery : IRequest<PagedResult<NovelBasicDTO>>
{
    public int PageNumber { get; set; } = 1;
    public int PageSize { get; set; } = 100;
}
