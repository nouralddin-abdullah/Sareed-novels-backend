using Application.Wallet.DTOs;
using AutoMapper;
using Domain.Repositories;
using MediatR;

namespace Application.Wallet.Queries.GetPendingRechargeRequests;

public class GetPendingRechargeRequestsQueryHandler(
    IRechargeRequestRepository rechargeRepository,
    IMapper mapper) : IRequestHandler<GetPendingRechargeRequestsQuery, (IEnumerable<RechargeRequestDto>, int)>
{
    public async Task<(IEnumerable<RechargeRequestDto>, int)> Handle(GetPendingRechargeRequestsQuery request, CancellationToken cancellationToken)
    {
        var (requests, totalCount) = await rechargeRepository.GetPendingRequestsAsync(
            request.PageNumber,
            request.PageSize
        );
        
        var dtos = mapper.Map<IEnumerable<RechargeRequestDto>>(requests);
        
        return (dtos, totalCount);
    }
}
