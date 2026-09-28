using Application.Services;
using Application.Wallet.DTOs;
using AutoMapper;
using Domain.Repositories;
using MediatR;
using Application.Common;

namespace Application.Wallet.Queries.GetPendingWithdrawalRequests;

public class GetPendingWithdrawalRequestsQueryHandler(
    IWithdrawalRequestRepository withdrawalRepository,
    IPointTransactionRepository ledger,
    IWalletService walletService,
    TimeProvider time,
    IMapper mapper) : IRequestHandler<GetPendingWithdrawalRequestsQuery, (IEnumerable<WithdrawalRequestDto>, int)>
{
    /// <summary>How far back the payout review shows a requester's earning reversals, and how many at most.</summary>
    public static readonly TimeSpan ReversalsWindow = TimeSpan.FromDays(90);
    public const int ReversalsShown = 10;

    public async Task<(IEnumerable<WithdrawalRequestDto>, int)> Handle(GetPendingWithdrawalRequestsQuery request, CancellationToken cancellationToken)
    {
        var (pageNumber, pageSize) = Paging.Clamp(request.PageNumber, request.PageSize);
        var (requests, totalCount) = await withdrawalRepository.GetPendingRequestsAsync(pageNumber, pageSize);

        var dtos = mapper.Map<List<WithdrawalRequestDto>>(requests);

        // For the manual payout review (#22): what each requester can be paid, and any earnings a refund took back.
        var userIds = dtos.Select(d => d.UserId!).Distinct().ToList();
        var payable = new Dictionary<string, decimal>();
        foreach (var userId in userIds)
        {
            payable[userId] = (await walletService.GetWithdrawableAsync(userId)).Payable;
        }

        var since = time.GetUtcNow().UtcDateTime - ReversalsWindow;
        var reversals = (await ledger.GetEarningReversalsAsync(userIds, since, ReversalsShown)).ToLookup(t => t.UserId);

        foreach (var dto in dtos)
        {
            dto.RequesterWithdrawable = payable[dto.UserId!];
            dto.RecentEarningReversals = reversals[dto.UserId!]
                .Select(t => new EarningReversalDto
                {
                    Id = t.Id,
                    Amount = t.Amount,
                    Description = t.Description,
                    CreatedAt = DateTime.SpecifyKind(t.CreatedAt, DateTimeKind.Utc),
                    PurchaseId = t.RelatedRequestId,
                    ReversedTransactionId = t.ReversedTransactionId,
                    NovelId = t.NovelId
                })
                .ToList();
        }

        return (dtos, totalCount);
    }
}
