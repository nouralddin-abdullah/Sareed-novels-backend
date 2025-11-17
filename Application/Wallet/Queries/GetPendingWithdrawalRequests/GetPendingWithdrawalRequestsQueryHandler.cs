using Application.Wallet.DTOs;
using AutoMapper;
using Domain.Repositories;
using MediatR;

namespace Application.Wallet.Queries.GetPendingWithdrawalRequests;

public class GetPendingWithdrawalRequestsQueryHandler(
    IWithdrawalRequestRepository withdrawalRepository,
    IMapper mapper) : IRequestHandler<GetPendingWithdrawalRequestsQuery, (IEnumerable<WithdrawalRequestDto>, int)>
{
    public async Task<(IEnumerable<WithdrawalRequestDto>, int)> Handle(GetPendingWithdrawalRequestsQuery request, CancellationToken cancellationToken)
    {
        var (requests, totalCount) = await withdrawalRepository.GetPendingRequestsAsync(
            request.PageNumber,
            request.PageSize
        );
        
        var dtos = mapper.Map<IEnumerable<WithdrawalRequestDto>>(requests);
        
        return (dtos, totalCount);
    }
}
