using Domain.Entities;

namespace Domain.Repositories;

/// <summary>A user's earnings at a given moment (#22).</summary>
/// <param name="Released">Earnings whose hold has ended.</param>
/// <param name="ReversedReleased">What EarningReversed rows took back of released earnings (or of a row that can't be
/// found, counted here to be safe).</param>
/// <param name="Held">Earnings still on hold, less what was reversed of them.</param>
/// <param name="NextReleaseAt">The earliest release among held earnings with something left; null when there is none.</param>
public sealed record EarningsTotals(decimal Released, decimal ReversedReleased, decimal Held, DateTime? NextReleaseAt);

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

    /// <summary>The user's earnings at <paramref name="now"/>: released, held, and what refunds reversed of each.</summary>
    Task<EarningsTotals> GetEarningsTotalsAsync(string userId, DateTime now);

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
