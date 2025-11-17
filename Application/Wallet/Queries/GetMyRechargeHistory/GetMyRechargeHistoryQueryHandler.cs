using Application.Users;
using Application.Wallet.DTOs;
using AutoMapper;
using Domain.Exceptions;
using Domain.Repositories;
using MediatR;

namespace Application.Wallet.Queries.GetMyRechargeHistory;

public class GetMyRechargeHistoryQueryHandler(
    IUserContext userContext,
    IRechargeRequestRepository rechargeRepository,
    IMapper mapper) : IRequestHandler<GetMyRechargeHistoryQuery, (IEnumerable<RechargeRequestDto>, int)>
{
    public async Task<(IEnumerable<RechargeRequestDto>, int)> Handle(GetMyRechargeHistoryQuery request, CancellationToken cancellationToken)
    {
        var currentUser = userContext.GetCurrentUser() ?? throw new ForbidException("User not signed in");
        
        var (requests, totalCount) = await rechargeRepository.GetUserRequestsAsync(
            currentUser.Id,
            request.PageNumber,
            request.PageSize,
            request.Status
        );
        
        var dtos = mapper.Map<IEnumerable<RechargeRequestDto>>(requests);
        
        return (dtos, totalCount);
    }
}
