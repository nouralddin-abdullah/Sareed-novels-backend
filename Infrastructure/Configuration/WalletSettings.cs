namespace Infrastructure.Configuration;

/// <summary>The wallet (section <c>Wallet</c>). See README.md, "Wallet: what can be withdrawn".</summary>
public class WalletSettings
{
    public const string SectionName = "Wallet";

    public const int DefaultEarningsHoldDays = 30;

    /// <summary>
    /// Days an earning (a gift or privilege subscription received) is held before it can be withdrawn, from 0 to 365;
    /// 30 by default, the window of Google's voided purchases list, so a refund is seen while its points are still held.
    /// A change applies to earnings credited from then on. Anything else stops the API at startup (ValidateOnStart).
    /// </summary>
    public int EarningsHoldDays { get; set; } = DefaultEarningsHoldDays;
}
