using Application.Common;
using Application.Gifts.DTOs;
using AutoMapper;
using Domain.Repositories;
using MediatR;

namespace Application.Gifts.Queries.GetNovelGifts;

public class GetNovelGiftsQueryHandler(
    IGiftTransactionRepository giftTransactionRepository,
    IMapper mapper) : IRequestHandler<GetNovelGiftsQuery, PagedResult<GiftTransactionDto>>
{
    public async Task<PagedResult<GiftTransactionDto>> Handle(GetNovelGiftsQuery request, CancellationToken cancellationToken)
    {
        // Out-of-range paging used to throw (page 0) or divide by zero (size 0).
        var pageNumber = Math.Max(1, request.PageNumber);
        var pageSize = Math.Clamp(request.PageSize, 1, 100);

        var (transactions, totalCount) = await giftTransactionRepository.GetTransactionsByNovel(
            request.NovelId,
            pageNumber,
            pageSize
        );

        var transactionDtos = mapper.Map<List<GiftTransactionDto>>(transactions);

        return new PagedResult<GiftTransactionDto>(
            transactionDtos,
            totalCount,
            pageSize: pageSize,
            pageNumber: pageNumber
        );
    }
}
