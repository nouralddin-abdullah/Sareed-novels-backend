using Application.Users;
using Application.Wallet.DTOs;
using AutoMapper;
using Domain.Exceptions;
using Domain.Repositories;
using MediatR;
using Application.Common;

namespace Application.Wallet.Queries.GetMyWithdrawalHistory;

public class GetMyWithdrawalHistoryQueryHandler(
    IUserContext userContext,
    IWithdrawalRequestRepository withdrawalRepository,
    IMapper mapper) : IRequestHandler<GetMyWithdrawalHistoryQuery, (IEnumerable<WithdrawalRequestDto>, int)>
{
    public async Task<(IEnumerable<WithdrawalRequestDto>, int)> Handle(GetMyWithdrawalHistoryQuery request, CancellationToken cancellationToken)
    {
        var currentUser = userContext.GetCurrentUser() ?? throw new ForbidException("User not signed in", "NotSignedIn");
        var (pageNumber, pageSize) = Paging.Clamp(request.PageNumber, request.PageSize);
        
        var (requests, totalCount) = await withdrawalRepository.GetUserRequestsAsync(
            currentUser.Id,
            pageNumber,
            pageSize,
            request.Status
        );
        
        var dtos = mapper.Map<IEnumerable<WithdrawalRequestDto>>(requests);
        
        return (dtos, totalCount);
    }
}
