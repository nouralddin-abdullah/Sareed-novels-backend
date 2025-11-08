using Application.Common;
using Application.ReadingLists.DTOs;
using MediatR;

namespace Application.ReadingLists.Queries.GetFollowedReadingLists;

public class GetFollowedReadingListsQuery : IRequest<PagedResult<ReadingListPreviewDTO>>
{
    public int PageNumber { get; set; } = 1;
    public int PageSize { get; set; } = 12;
}
