using Application.Common;
using Domain.Constants;

namespace Application.Wallet;

/// <summary>
/// The <c>types</c> filter of GET /api/wallet/transactions (#92), read as the notifications' (#78): TransactionType
/// names (<see cref="TransactionType.All"/>, the first week's Recharge and Withdrawal included), comma-separated, matched
/// whatever their letter case and the spaces around them (<see cref="NameFilter"/>). Unknown names are ignored, and a
/// filter that names no known type (or is empty, or missing) filters nothing: every type.
/// </summary>
public static class TransactionTypeFilter
{
    private static readonly NameFilter Types = new(TransactionType.All);

    /// <summary>
    /// The types <paramref name="values"/> name, each once and spelled as <see cref="TransactionType"/> spells it; null
    /// when they name no known type: every type.
    /// </summary>
    public static IReadOnlyList<string>? Parse(IEnumerable<string?>? values) => Types.ForList(values);
}
