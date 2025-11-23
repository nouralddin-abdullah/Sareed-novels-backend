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
        var (transactions, totalCount) = await giftTransactionRepository.GetTransactionsByNovel(
            request.NovelId,
            request.PageNumber,
            request.PageSize
        );

        var transactionDtos = mapper.Map<List<GiftTransactionDto>>(transactions);

        return new PagedResult<GiftTransactionDto>(
            transactionDtos,
            totalCount,
            request.PageNumber,
            request.PageSize
        );
    }
}
