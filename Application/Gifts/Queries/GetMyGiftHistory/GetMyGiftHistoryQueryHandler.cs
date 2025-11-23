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

        var (transactions, totalCount) = await giftTransactionRepository.GetTransactionsBySender(
            currentUser.Id,
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
