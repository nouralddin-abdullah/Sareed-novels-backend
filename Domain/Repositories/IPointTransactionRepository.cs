using Domain.Entities;

namespace Domain.Repositories;

/// <summary>
/// A ledger row as the wallet's pools read it (#27): what it was, how much, when, and for an earning when it is released or
/// for the author's side of a reversal which earning it took back.
/// </summary>
public sealed record LedgerEntry(
    Guid Id,
    string Type,
    decimal Amount,
    decimal BalanceBefore,
    decimal BalanceAfter,
    DateTime CreatedAt,
    DateTime? AvailableAt = null,
    Guid? ReversedTransactionId = null);

/// <summary>An author's earning still on hold that a reader's gift or privilege subscription paid for (#22 rule 4).</summary>
/// <param name="EarningId">The GiftReceived or PrivilegeRevenue row.</param>
/// <param name="Remaining">Its amount less what earlier reversals already took back.</param>
/// <param name="PaidAt">When the reader paid (the GiftSent or PrivilegeSubscription row).</param>
public sealed record HeldEarning(
    Guid EarningId,
    string AuthorId,
    decimal Remaining,
    Guid? NovelId,
    Guid? GiftId,
    int? GiftCount,
    DateTime PaidAt);

public interface IPointTransactionRepository
{
    Task<PointTransaction> CreateAsync(PointTransaction transaction);
    Task<(IEnumerable<PointTransaction>, int)> GetUserTransactionsAsync(string userId, int pageNumber, int pageSize);

    /// <summary>Every ledger row of the user, in no particular order (the wallet's pools order them, #27).</summary>
    Task<IReadOnlyList<LedgerEntry>> GetLedgerAsync(string userId);

    /// <summary>
    /// The earnings still on hold at <paramref name="now"/> that <paramref name="buyerId"/> paid for with gifts or privilege
    /// subscriptions since <paramref name="paidSince"/>, newest payment first, with what is left of each. Payments from
    /// before #22 aren't paired with their earning (and those earnings are all released anyway).
    /// </summary>
    Task<IReadOnlyList<HeldEarning>> GetHeldEarningsPaidByAsync(string buyerId, DateTime paidSince, DateTime now);

    /// <summary>The EarningReversed rows of these users since <paramref name="since"/>, newest first, at most
    /// <paramref name="perUser"/> each.</summary>
    Task<IReadOnlyList<PointTransaction>> GetEarningReversalsAsync(IReadOnlyCollection<string> userIds, DateTime since, int perUser);
}
