using System.Globalization;
using Application.Services;
using Application.Users;
using Application.Wallet.DTOs;
using Domain.Exceptions;
using Domain.Repositories;
using MediatR;

namespace Application.Wallet.Queries.GetMyEarnings;

/// <summary>
/// The earnings summary (#78) in a fixed number of queries: the wallet's figures (<see cref="IWalletService.GetWithdrawableAsync"/>,
/// as GET /api/wallet), her earning rows summed in SQL by novel, month, type and sign, the supporters of each novel, and
/// the novels' names.
/// </summary>
public class GetMyEarningsQueryHandler(
    IUserContext userContext,
    IWalletService walletService,
    IPointTransactionRepository transactionRepository,
    INovelsRepository novelsRepository) : IRequestHandler<GetMyEarningsQuery, EarningsSummaryDto>
{
    /// <summary>The title of the entry for earnings whose ledger rows don't say their novel (rows from before #17).</summary>
    public const string NoNovelTitle = "أرباح بلا رواية محددة";

    public async Task<EarningsSummaryDto> Handle(GetMyEarningsQuery request, CancellationToken cancellationToken)
    {
        var currentUser = userContext.GetCurrentUser() ?? throw new ForbidException("سجّل الدخول للمتابعة", "NotSignedIn");

        // totalEarned, pendingEarnings and nextReleaseAt are GET /api/wallet's, from the same call; its moment is the
        // "now" of the months too.
        var wallet = await walletService.GetWithdrawableAsync(currentUser.Id);
        var groups = await transactionRepository.GetEarningsGroupsAsync(currentUser.Id);
        var supporters = await transactionRepository.GetSupportersByNovelAsync(currentUser.Id);
        var breakdown = EarningsBreakdown.From(groups, wallet.AsOf);
        var novels = await novelsRepository.GetCardsIncludingDeletedAsync(
            breakdown.ByNovel.Select(n => n.NovelId).OfType<Guid>().ToList());

        return new EarningsSummaryDto
        {
            TotalEarned = wallet.TotalEarned,
            PendingEarnings = wallet.PendingEarnings,
            NextReleaseAt = wallet.NextReleaseAt,
            ByNovel = breakdown.ByNovel.Select(entry =>
            {
                // A novel id without a novel can't happen (novels are only ever soft-deleted): it would keep its id,
                // unnamed, and count as deleted.
                var novel = entry.NovelId is { } id ? novels.GetValueOrDefault(id) : null;
                return new NovelEarningsDto
                {
                    NovelId = entry.NovelId,
                    NovelSlug = novel?.Slug,
                    NovelTitle = entry.NovelId is null ? NoNovelTitle : novel?.Title,
                    CoverImageUrl = novel?.CoverImageUrl,
                    IsDeleted = entry.NovelId is null ? null : novel?.IsDeleted ?? true,
                    IsDraft = entry.NovelId is null ? null : novel is { IsDraft: true, IsDeleted: false },
                    Gifts = entry.Sums.Gifts,
                    Privileges = entry.Sums.Privileges,
                    Reversed = entry.Sums.Reversed,
                    Total = entry.Sums.Total,
                    SupportersCount = supporters.FirstOrDefault(s => s.NovelId == entry.NovelId)?.Count ?? 0
                };
            }).ToList(),
            ByMonth = breakdown.ByMonth.Select(month => new MonthEarningsDto
            {
                Month = month.Month.ToString("yyyy-MM", CultureInfo.InvariantCulture),
                Gifts = month.Sums.Gifts,
                Privileges = month.Sums.Privileges,
                Reversed = month.Sums.Reversed,
                Total = month.Sums.Total
            }).ToList()
        };
    }
}
