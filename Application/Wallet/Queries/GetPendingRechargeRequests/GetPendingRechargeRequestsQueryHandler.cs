using Application.Wallet.DTOs;
using AutoMapper;
using Domain.Repositories;
using MediatR;
using Application.Common;

namespace Application.Wallet.Queries.GetPendingRechargeRequests;

public class GetPendingRechargeRequestsQueryHandler(
    IRechargeRequestRepository rechargeRepository,
    IMapper mapper) : IRequestHandler<GetPendingRechargeRequestsQuery, (IEnumerable<RechargeRequestDto>, int)>
{
    public async Task<(IEnumerable<RechargeRequestDto>, int)> Handle(GetPendingRechargeRequestsQuery request, CancellationToken cancellationToken)
    {
        var (pageNumber, pageSize) = Paging.Clamp(request.PageNumber, request.PageSize);
        var (requests, totalCount) = await rechargeRepository.GetPendingRequestsAsync(pageNumber, pageSize);
        
        var dtos = mapper.Map<IEnumerable<RechargeRequestDto>>(requests);
        
        return (dtos, totalCount);
    }
}
