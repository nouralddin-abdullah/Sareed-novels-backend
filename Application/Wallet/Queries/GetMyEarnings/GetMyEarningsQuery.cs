using Application.Wallet.DTOs;
using MediatR;

namespace Application.Wallet.Queries.GetMyEarnings;

/// <summary>The signed-in member's earnings summary (#78): writer mode's earnings screen.</summary>
public class GetMyEarningsQuery : IRequest<EarningsSummaryDto>
{
}
