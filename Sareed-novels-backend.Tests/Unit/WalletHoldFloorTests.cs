using Infrastructure.Configuration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using Sareed_novels_backend.Tests.Integration;

namespace Sareed_novels_backend.Tests.Unit;

/// <summary>
/// Wallet:EarningsHoldDays can't silently turn the refund clawback off (#27): below 30 days it becomes 30 in Production
/// and stays as set elsewhere, with a warning either way; outside 0..365 the API doesn't start.
/// </summary>
public class WalletHoldFloorTests
{
    private static (WalletSettings Settings, ListLogger<WalletHoldFloor> Log) Read(string environment, string? holdDays)
    {
        var values = new Dictionary<string, string?>();
        if (holdDays is not null)
        {
            values["Wallet:EarningsHoldDays"] = holdDays;
        }
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(values).Build();
        var host = Substitute.For<IHostEnvironment>();
        host.EnvironmentName.Returns(environment);
        var log = new ListLogger<WalletHoldFloor>();

        using var provider = new ServiceCollection()
            .AddSingleton(host)
            .AddSingleton<ILogger<WalletHoldFloor>>(log)
            .AddWalletSettings(configuration)
            .BuildServiceProvider();
        return (provider.GetRequiredService<IOptions<WalletSettings>>().Value, log);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("29")]
    public void Production_holds_earnings_30_days_at_least_and_says_so(string configured)
    {
        var (settings, log) = Read(Environments.Production, configured);

        Assert.Equal(30, settings.EarningsHoldDays);
        var warning = Assert.Single(log.Entries);
        Assert.Equal(LogLevel.Warning, warning.Level);
        Assert.Equal(int.Parse(configured), warning.Values["Configured"]);
        Assert.Contains("Earnings are held 30 days", warning.Message);
    }

    [Theory]
    [InlineData("Development", "7")]
    [InlineData("Testing", "0")]
    public void Elsewhere_a_shorter_hold_is_kept_with_a_warning(string environment, string configured)
    {
        var (settings, log) = Read(environment, configured);

        Assert.Equal(int.Parse(configured), settings.EarningsHoldDays);
        var warning = Assert.Single(log.Entries);
        Assert.Equal(LogLevel.Warning, warning.Level);
        Assert.Contains("Allowed outside Production only", warning.Message);
    }

    [Theory]
    [InlineData(null, 30)]
    [InlineData("30", 30)]
    [InlineData("90", 90)]
    public void A_hold_of_30_days_or_more_is_kept_quietly(string? configured, int expected)
    {
        var (settings, log) = Read(Environments.Production, configured);

        Assert.Equal(expected, settings.EarningsHoldDays);
        Assert.Empty(log.Entries);
    }

    [Theory]
    [InlineData("Production", "-1")]
    [InlineData("Development", "-30")]
    [InlineData("Production", "366")]
    public void A_negative_hold_or_one_over_a_year_stops_the_api(string environment, string configured)
    {
        var error = Assert.Throws<OptionsValidationException>(() => Read(environment, configured));
        Assert.Contains("Wallet:EarningsHoldDays must be a whole number of days from 0 to 365", error.Message);
    }
}
