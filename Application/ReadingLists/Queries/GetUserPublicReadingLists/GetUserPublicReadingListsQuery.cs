using Application.Common;
using Application.ReadingLists.DTOs;
using MediatR;

namespace Application.ReadingLists.Queries.GetUserPublicReadingLists;

public class GetUserPublicReadingListsQuery(string userName) : IRequest<PagedResult<ReadingListPreviewDTO>>
{
    public string UserName { get; set; } = userName;
    public int PageNumber { get; set; } = 1;
    public int PageSize { get; set; } = 12;
}
