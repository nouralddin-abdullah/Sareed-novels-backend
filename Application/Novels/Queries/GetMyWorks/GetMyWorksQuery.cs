using Application.Common;
using Application.Novels.DTOS;
using MediatR;

namespace Application.Novels.Queries.GetMyWorks;

public class GetMyWorksQuery : IRequest<PagedResult<MyWorksDTO>>
{
    public int PageSize { get; set; } = 10;
    public int PageNumber { get; set; } = 1;
}
