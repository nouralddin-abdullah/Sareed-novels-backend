using Application.Common;
using Application.Gifts.DTOs;
using MediatR;

namespace Application.Gifts.Queries.GetAllGifts;

public class GetAllGiftsQuery(int pageNumber, int pageSize) : IRequest<PagedResult<GiftDto>>
{
    public int PageNumber { get; set; } = pageNumber;
    public int PageSize { get; set; } = pageSize;
}
