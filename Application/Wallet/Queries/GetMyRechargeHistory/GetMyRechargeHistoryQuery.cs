using Application.Wallet.DTOs;
using MediatR;

namespace Application.Wallet.Queries.GetMyRechargeHistory;

public class GetMyRechargeHistoryQuery : IRequest<(IEnumerable<RechargeRequestDto>, int)>
{
    public int PageNumber { get; set; } = 1;
    public int PageSize { get; set; } = 10;
    public string? Status { get; set; }
}
