using Application.Common;
using Application.Gifts.DTOs;
using MediatR;

namespace Application.Gifts.Queries.GetMyGiftHistory;

public class GetMyGiftHistoryQuery(int pageNumber, int pageSize) : IRequest<PagedResult<GiftTransactionDto>>
{
    public int PageNumber { get; set; } = pageNumber;
    public int PageSize { get; set; } = pageSize;
}
