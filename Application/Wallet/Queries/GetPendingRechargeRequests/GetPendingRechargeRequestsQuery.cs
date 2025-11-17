using Application.Wallet.DTOs;
using MediatR;

namespace Application.Wallet.Queries.GetPendingRechargeRequests;

public class GetPendingRechargeRequestsQuery : IRequest<(IEnumerable<RechargeRequestDto>, int)>
{
    public int PageNumber { get; set; } = 1;
    public int PageSize { get; set; } = 20;
}
