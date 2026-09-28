using Domain.Entities;

namespace Domain.Repositories;

/// <summary>A user's earnings at a given moment (#22).</summary>
/// <param name="Released">Earnings whose hold has ended.</param>
/// <param name="ReversedReleased">What EarningReversed rows took back of released earnings (or of a row that can't be
/// found, counted here to be safe).</param>
/// <param name="Held">Earnings still on hold, less what was reversed of them.</param>
/// <param name="NextReleaseAt">The earliest release among held earnings with something left; null when there is none.</param>
public sealed record EarningsTotals(decimal Released, decimal ReversedReleased, decimal Held, DateTime? NextReleaseAt);

public interface IPointTransactionRepository
{
    Task<PointTransaction> CreateAsync(PointTransaction transaction);
    Task<(IEnumerable<PointTransaction>, int)> GetUserTransactionsAsync(string userId, int pageNumber, int pageSize);

    /// <summary>The user's earnings at <paramref name="now"/>: released, held, and what refunds reversed of each.</summary>
    Task<EarningsTotals> GetEarningsTotalsAsync(string userId, DateTime now);

    /// <summary>The EarningReversed rows of these users since <paramref name="since"/>, newest first, at most
    /// <paramref name="perUser"/> each.</summary>
    Task<IReadOnlyList<PointTransaction>> GetEarningReversalsAsync(IReadOnlyCollection<string> userIds, DateTime since, int perUser);
}
