using Application.Wallet.DTOs;
using MediatR;

namespace Application.Wallet.Queries.GetMyTransactionHistory;

public class GetMyTransactionHistoryQuery : IRequest<(IEnumerable<PointTransactionDto>, int)>
{
    public int PageNumber { get; set; } = 1;
    public int PageSize { get; set; } = 20;

    /// <summary>The types filter as sent (#92): comma-separated TransactionType names (<see cref="TransactionTypeFilter"/>).</summary>
    public IReadOnlyList<string>? Types { get; set; }
}
