using System.Reflection;
using Application.Common;
using Application.Wallet;
using Domain.Constants;

namespace Sareed_novels_backend.Tests.Unit;

/// <summary>
/// The types filter of GET /api/wallet/transactions (#92), read as the notifications' (#78): known names in any letter
/// case and with spaces around them, unknown ones ignored, and no known name at all meaning every type. Through the API:
/// Integration/WalletTransactionTypesHttpTests.
/// </summary>
public class TransactionTypeFilterTests
{
    /// <summary>What the app's earnings page asks for.</summary>
    private static readonly string[] EarningsPage = [TransactionType.GiftReceived, TransactionType.PrivilegeRevenue, TransactionType.EarningReversed];

    [Fact]
    public void All_lists_every_transaction_type_and_the_first_weeks_names()
    {
        var constants = typeof(TransactionType).GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(f => f.IsLiteral && f.FieldType == typeof(string))
            .Select(f => (string)f.GetRawConstantValue()!);

        Assert.Equal(constants.Order(), TransactionType.All.Order());
        Assert.Equal(TransactionType.All.Count, TransactionType.All.Distinct().Count());
        Assert.Contains("Recharge", TransactionType.All);
        Assert.Contains("Withdrawal", TransactionType.All);
    }

    [Theory]
    [InlineData("GiftReceived,PrivilegeRevenue,EarningReversed")]
    [InlineData(" giftreceived , PRIVILEGEREVENUE,earningReversed ")]
    [InlineData("GiftReceived,,PrivilegeRevenue,EarningReversed,")]
    [InlineData("GiftReceived,NotAType,PrivilegeRevenue,Gift Received,EarningReversed,GIFTRECEIVED")]
    public void Known_names_match_in_any_case_with_spaces_around_once_each_and_unknown_ones_are_ignored(string types) =>
        Assert.Equal(EarningsPage, TransactionTypeFilter.Parse([types]));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" , ,")]
    [InlineData("NotAType")]
    [InlineData("UnlockChapter,Gift")]
    [InlineData("Gift Received")]
    public void No_known_name_means_every_type(string? types)
    {
        Assert.Null(TransactionTypeFilter.Parse([types]));
        Assert.Null(TransactionTypeFilter.Parse(null));
        Assert.Null(TransactionTypeFilter.Parse([]));
    }

    [Fact]
    public void Every_type_can_be_named_and_the_parameter_may_repeat()
    {
        foreach (var type in TransactionType.All)
        {
            Assert.Equal([type], TransactionTypeFilter.Parse([type.ToUpperInvariant()]));
        }
        Assert.Equal(TransactionType.All, TransactionTypeFilter.Parse([string.Join(",", TransactionType.All)]));
        Assert.Equal(EarningsPage, TransactionTypeFilter.Parse(["giftreceived", "PrivilegeRevenue,NotAType", "EarningReversed,GiftReceived"]));
    }

    [Theory]
    [InlineData(null, false)]
    [InlineData("", false)]
    [InlineData(" ", false)]
    [InlineData(" , ,", false)]
    [InlineData("NotAType", true)]
    [InlineData(" ,GiftReceived", true)]
    public void Names_any_tells_a_filter_that_names_something_from_one_that_names_nothing(string? types, bool namesAny)
    {
        Assert.Equal(namesAny, NameFilter.NamesAny([types]));
        Assert.False(NameFilter.NamesAny(null));
        Assert.False(NameFilter.NamesAny([]));
        Assert.True(NameFilter.NamesAny(["", "x"]));
    }
}
