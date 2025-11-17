using Application.Users;
using Application.Wallet.DTOs;
using AutoMapper;
using Domain.Exceptions;
using Domain.Repositories;
using MediatR;

namespace Application.Wallet.Queries.GetMyWithdrawalHistory;

public class GetMyWithdrawalHistoryQueryHandler(
    IUserContext userContext,
    IWithdrawalRequestRepository withdrawalRepository,
    IMapper mapper) : IRequestHandler<GetMyWithdrawalHistoryQuery, (IEnumerable<WithdrawalRequestDto>, int)>
{
    public async Task<(IEnumerable<WithdrawalRequestDto>, int)> Handle(GetMyWithdrawalHistoryQuery request, CancellationToken cancellationToken)
    {
        var currentUser = userContext.GetCurrentUser() ?? throw new ForbidException("User not signed in");
        
        var (requests, totalCount) = await withdrawalRepository.GetUserRequestsAsync(
            currentUser.Id,
            request.PageNumber,
            request.PageSize,
            request.Status
        );
        
        var dtos = mapper.Map<IEnumerable<WithdrawalRequestDto>>(requests);
        
        return (dtos, totalCount);
    }
}
