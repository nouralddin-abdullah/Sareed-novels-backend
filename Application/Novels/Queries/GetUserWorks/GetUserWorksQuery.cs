using Application.Common;
using Application.Novels.DTOS;
using MediatR;

namespace Application.Novels.Queries.GetUserWorks;

public class GetUserWorksQuery(string userId, int pageNumber, int pageSize) : IRequest<PagedResult<MyWorksDTO>>
{
    public string UserId { get; set; } = userId;
    public int PageNumber { get; set; } = pageNumber;
    public int PageSize { get; set; } = pageSize;
}
