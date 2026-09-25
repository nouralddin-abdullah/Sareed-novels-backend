using Application.Common;
using Application.Gifts.DTOs;
using Application.Users;
using AutoMapper;
using Domain.Exceptions;
using Domain.Repositories;
using MediatR;

namespace Application.Gifts.Queries.GetMyGiftHistory;

public class GetMyGiftHistoryQueryHandler(
    IGiftTransactionRepository giftTransactionRepository,
    IUserContext userContext,
    IMapper mapper) : IRequestHandler<GetMyGiftHistoryQuery, PagedResult<GiftTransactionDto>>
{
    public async Task<PagedResult<GiftTransactionDto>> Handle(GetMyGiftHistoryQuery request, CancellationToken cancellationToken)
    {
        var currentUser = userContext.GetCurrentUser()
            ?? throw new ForbidException("User not authenticated");

        // Out-of-range paging used to throw (page 0) or divide by zero (size 0).
        var pageNumber = Math.Max(1, request.PageNumber);
        var pageSize = Math.Clamp(request.PageSize, 1, 100);

        var (transactions, totalCount) = await giftTransactionRepository.GetTransactionsBySender(
            currentUser.Id,
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
