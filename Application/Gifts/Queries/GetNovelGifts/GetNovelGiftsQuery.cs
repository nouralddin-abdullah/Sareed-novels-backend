using Application.Common;
using Application.Gifts.DTOs;
using MediatR;

namespace Application.Gifts.Queries.GetNovelGifts;

public class GetNovelGiftsQuery(Guid novelId, int pageNumber, int pageSize) : IRequest<PagedResult<GiftTransactionDto>>
{
    public Guid NovelId { get; set; } = novelId;
    public int PageNumber { get; set; } = pageNumber;
    public int PageSize { get; set; } = pageSize;
}
