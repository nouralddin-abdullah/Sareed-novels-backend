using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Infrastructure.Configuration;

/// <summary>The wallet (section <c>Wallet</c>). See README.md, "Wallet: what can be withdrawn".</summary>
public class WalletSettings
{
    public const string SectionName = "Wallet";

    public const int DefaultEarningsHoldDays = 30;

    /// <summary>
    /// The shortest hold Production accepts (#27): Google lists voided purchases for 30 days, and the refund clawback only
    /// takes back earnings still on hold, so a shorter hold would let refunded points be withdrawn before the refund is
    /// seen.
    /// </summary>
    public const int MinimumEarningsHoldDays = 30;

    /// <summary>The longest hold the setting accepts.</summary>
    public const int MaxEarningsHoldDays = 365;

    /// <summary>
    /// Days an earning (a gift or privilege subscription received) is held before it can be withdrawn, from 0 to 365;
    /// 30 by default, the window of Google's voided purchases list, so a refund is seen while its points are still held.
    /// Below 30 it is raised to 30 in Production, and allowed elsewhere (tests use short holds), with a warning at startup
    /// either way. A change applies to earnings credited from then on. A value outside 0..365 stops the API at startup.
    /// </summary>
    public int EarningsHoldDays { get; set; } = DefaultEarningsHoldDays;
}

/// <summary>
/// Keeps Wallet:EarningsHoldDays from silently turning the refund clawback off (#27): below
/// <see cref="WalletSettings.MinimumEarningsHoldDays"/> it becomes that in Production and stays as set elsewhere, and
/// either way the API logs a warning when the settings are read at startup. Negative values are left to the validation,
/// which refuses them.
/// </summary>
internal sealed class WalletHoldFloor(IHostEnvironment environment, ILogger<WalletHoldFloor> logger) : IPostConfigureOptions<WalletSettings>
{
    public void PostConfigure(string? name, WalletSettings settings)
    {
        var configured = settings.EarningsHoldDays;
        if (configured is < 0 or >= WalletSettings.MinimumEarningsHoldDays)
        {
            return;
        }

        if (environment.IsProduction())
        {
            settings.EarningsHoldDays = WalletSettings.MinimumEarningsHoldDays;
            logger.LogWarning(
                "Wallet:EarningsHoldDays is {Configured}, below the {Minimum} days Google lists voided purchases for: refunded points could be withdrawn before the refund is seen. Earnings are held {Minimum} days",
                configured, WalletSettings.MinimumEarningsHoldDays, WalletSettings.MinimumEarningsHoldDays);
        }
        else
        {
            logger.LogWarning(
                "Wallet:EarningsHoldDays is {Configured}, below the {Minimum} days Google lists voided purchases for: a refund seen after the hold can't take its earnings back. Allowed outside Production only ({Environment})",
                configured, WalletSettings.MinimumEarningsHoldDays, environment.EnvironmentName);
        }
    }
}

public static class WalletSettingsServiceCollectionExtensions
{
    /// <summary>
    /// Wallet:EarningsHoldDays (#22, #27): bound from configuration, raised to 30 in Production when set lower (a warning
    /// either way), and anything outside 0..365 stops the API at startup, rather than holding earnings for a length
    /// nobody chose.
    /// </summary>
    public static IServiceCollection AddWalletSettings(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<WalletSettings>()
            .Bind(configuration.GetSection(WalletSettings.SectionName))
            .Validate(s => s.EarningsHoldDays is >= 0 and <= WalletSettings.MaxEarningsHoldDays,
                "Wallet:EarningsHoldDays must be a whole number of days from 0 to 365")
            .ValidateOnStart();
        services.AddSingleton<IPostConfigureOptions<WalletSettings>, WalletHoldFloor>();
        return services;
    }
}
