using Application.Wallet.DTOs;
using AutoMapper;
using Domain.Repositories;
using MediatR;
using Application.Common;

namespace Application.Wallet.Queries.GetPendingWithdrawalRequests;

public class GetPendingWithdrawalRequestsQueryHandler(
    IWithdrawalRequestRepository withdrawalRepository,
    IMapper mapper) : IRequestHandler<GetPendingWithdrawalRequestsQuery, (IEnumerable<WithdrawalRequestDto>, int)>
{
    public async Task<(IEnumerable<WithdrawalRequestDto>, int)> Handle(GetPendingWithdrawalRequestsQuery request, CancellationToken cancellationToken)
    {
        var (pageNumber, pageSize) = Paging.Clamp(request.PageNumber, request.PageSize);
        var (requests, totalCount) = await withdrawalRepository.GetPendingRequestsAsync(pageNumber, pageSize);
        
        var dtos = mapper.Map<IEnumerable<WithdrawalRequestDto>>(requests);
        
        return (dtos, totalCount);
    }
}
