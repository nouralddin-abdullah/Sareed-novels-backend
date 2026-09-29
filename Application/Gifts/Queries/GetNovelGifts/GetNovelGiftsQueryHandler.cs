using Application.Common;
using Application.Gifts.DTOs;
using Application.Users;
using AutoMapper;
using Domain.Repositories;
using MediatR;

namespace Application.Gifts.Queries.GetNovelGifts;

public class GetNovelGiftsQueryHandler(
    IGiftTransactionRepository giftTransactionRepository,
    IUserBlocksRepository blocksRepository,
    IUserContext userContext,
    IMapper mapper) : IRequestHandler<GetNovelGiftsQuery, PagedResult<GiftTransactionDto>>
{
    public async Task<PagedResult<GiftTransactionDto>> Handle(GetNovelGiftsQuery request, CancellationToken cancellationToken)
    {
        // Out-of-range paging used to throw (page 0) or divide by zero (size 0).
        var (pageNumber, pageSize) = Paging.Clamp(request.PageNumber, request.PageSize, GiftPaging.MaxPageSize);

        var (transactions, totalCount) = await giftTransactionRepository.GetTransactionsByNovel(
            request.NovelId,
            pageNumber,
            pageSize
        );

        var records = transactions.ToList();
        var transactionDtos = mapper.Map<List<GiftTransactionDto>>(records);

        // The messages are public (#31), except between a signed-in viewer and a sender who blocked each other, either
        // way: then it is null, as blocks keep comments apart. The gift itself stays listed.
        var viewer = userContext.GetCurrentUser();
        var senders = records.Where(t => t.Message != null).Select(t => t.SenderId).ToList();
        if (viewer is not null && senders.Count > 0)
        {
            var blocked = await blocksRepository.GetBlockedEitherWayAsync(viewer.Id, senders, cancellationToken);
            for (var i = 0; i < records.Count && i < transactionDtos.Count; i++)
            {
                if (blocked.Contains(records[i].SenderId))
                {
                    transactionDtos[i].Message = null;
                }
            }
        }

        return new PagedResult<GiftTransactionDto>(
            transactionDtos,
            totalCount,
            pageSize: pageSize,
            pageNumber: pageNumber
        );
    }
}
