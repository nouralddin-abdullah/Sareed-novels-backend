using Application.Wallet.DTOs;
using MediatR;

namespace Application.Wallet.Queries.GetMyTransactionHistory;

public class GetMyTransactionHistoryQuery : IRequest<(IEnumerable<PointTransactionDto>, int)>
{
    public int PageNumber { get; set; } = 1;
    public int PageSize { get; set; } = 20;
}
