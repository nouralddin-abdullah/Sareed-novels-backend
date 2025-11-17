using Application.Wallet.DTOs;
using MediatR;

namespace Application.Wallet.Queries.GetPendingWithdrawalRequests;

public class GetPendingWithdrawalRequestsQuery : IRequest<(IEnumerable<WithdrawalRequestDto>, int)>
{
    public int PageNumber { get; set; } = 1;
    public int PageSize { get; set; } = 20;
}
