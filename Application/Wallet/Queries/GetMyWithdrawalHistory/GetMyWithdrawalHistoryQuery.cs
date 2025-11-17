using Application.Wallet.DTOs;
using MediatR;

namespace Application.Wallet.Queries.GetMyWithdrawalHistory;

public class GetMyWithdrawalHistoryQuery : IRequest<(IEnumerable<WithdrawalRequestDto>, int)>
{
    public int PageNumber { get; set; } = 1;
    public int PageSize { get; set; } = 10;
    public string? Status { get; set; }
}
