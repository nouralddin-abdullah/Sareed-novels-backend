using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Infrastructure.Configuration;
using Infrastructure.PlayBilling;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Sareed_novels_backend.Tests.Unit;

/// <summary>Reading the Play Billing settings: the key (raw or base64), the catalog, and every way they disable billing.</summary>
public class PlayBillingConnectionTests
{
    private static readonly Dictionary<string, string?> DefaultProducts = new()
    {
        ["points_5000"] = "5000", ["points_500"] = "500", ["points_2500"] = "2500", ["points_1000"] = "1000"
    };

    /// <summary>A well-formed service account key with a throwaway RSA key; nothing ever uses it to call Google.</summary>
    private static string ServiceAccountJson(string type = "service_account")
    {
        using var rsa = RSA.Create(2048);
        return JsonSerializer.Serialize(new Dictionary<string, string>
        {
            ["type"] = type,
            ["project_id"] = "sard-test",
            ["private_key_id"] = "0123456789abcdef",
            ["private_key"] = rsa.ExportPkcs8PrivateKeyPem(),
            ["client_email"] = "play-billing@sard-test.iam.gserviceaccount.com",
            ["client_id"] = "123456789",
            ["client_secret"] = "never-echo-this-secret",
            ["token_uri"] = "https://oauth2.googleapis.com/token"
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void A_raw_or_base64_key_turns_billing_on_with_the_catalog_sorted_by_points(bool base64)
    {
        var json = ServiceAccountJson();
        var connection = PlayBillingConnection.FromSettings(new PlayBillingSettings
        {
            ServiceAccountJson = base64 ? Convert.ToBase64String(Encoding.UTF8.GetBytes(json)) : "  " + json + "\n",
            Products = DefaultProducts
        });

        Assert.True(connection.IsEnabled, connection.DisabledReason);
        Assert.Null(connection.DisabledReason);
        Assert.Equal("com.sardnovels.app", connection.PackageName);
        Assert.False(connection.AllowTestPurchases);
        Assert.Equal(["points_500", "points_1000", "points_2500", "points_5000"], connection.Products.Select(p => p.ProductId));
        Assert.Equal(1000, connection.PointsFor("points_1000"));
        Assert.Null(connection.PointsFor("POINTS_1000"));
        Assert.Null(connection.PointsFor("points_750"));
    }

    [Fact]
    public void Without_a_key_billing_is_off_and_says_why()
    {
        var connection = PlayBillingConnection.FromSettings(new PlayBillingSettings { Products = DefaultProducts });

        Assert.False(connection.IsEnabled);
        Assert.Null(connection.Tokens);
        Assert.Equal("PlayBilling:ServiceAccountJson is not set", connection.DisabledReason);
        Assert.Empty(connection.Products);
        Assert.Null(connection.PointsFor("points_1000"));
    }

    [Theory]
    [InlineData("%%% not base64 %%%")]
    [InlineData("{ \"type\": \"service_account\", ")]
    [InlineData("aGVsbG8gd29ybGQ=")] // base64 of "hello world"
    [InlineData("authorized_user")]
    public void A_broken_key_turns_billing_off_without_repeating_the_key(string value)
    {
        var key = value == "authorized_user" ? ServiceAccountJson(type: "authorized_user") : value;

        var connection = PlayBillingConnection.FromSettings(new PlayBillingSettings { ServiceAccountJson = key, Products = DefaultProducts });

        Assert.False(connection.IsEnabled);
        Assert.StartsWith("PlayBilling:ServiceAccountJson is not a valid service account key", connection.DisabledReason);
        Assert.DoesNotContain("never-echo-this-secret", connection.DisabledReason);
        Assert.DoesNotContain("PRIVATE KEY", connection.DisabledReason);
    }

    [Fact]
    public void Products_need_a_valid_Play_id_and_a_number_of_points_and_zero_switches_one_off()
    {
        var connection = PlayBillingConnection.FromSettings(new PlayBillingSettings
        {
            ServiceAccountJson = ServiceAccountJson(),
            Products = new Dictionary<string, string?>
            {
                ["points_500"] = "500", ["pack.v2_7"] = " 7 ", ["Points_Upper"] = "100", ["with space"] = "100", ["_underscore_first"] = "5",
                ["points_typo"] = "five hundred", ["points_empty"] = "", ["points_fraction"] = "1.5",
                ["points_off"] = "0", ["points_negative"] = "-10"
            }
        });

        Assert.True(connection.IsEnabled);
        Assert.Equal([("pack.v2_7", 7), ("points_500", 500)], connection.Products.Select(p => (p.ProductId, p.Points)));
        Assert.Equal(["Points_Upper=100", "with space=100", "_underscore_first=5", "points_typo=five hundred", "points_empty=", "points_fraction=1.5"],
            connection.IgnoredProducts);
    }

    [Fact]
    public void Without_any_product_billing_is_off()
    {
        var connection = PlayBillingConnection.FromSettings(new PlayBillingSettings
        {
            ServiceAccountJson = ServiceAccountJson(),
            Products = new Dictionary<string, string?> { ["points_500"] = "0" }
        });

        Assert.False(connection.IsEnabled);
        Assert.Equal("PlayBilling:Products has no product with positive points", connection.DisabledReason);
    }

    [Theory]
    [InlineData("sardnovels")]
    [InlineData("com..sardnovels")]
    [InlineData("com.sardnovels.app/other")]
    [InlineData("1com.sardnovels")]
    public void An_invalid_package_name_turns_billing_off(string packageName)
    {
        var connection = PlayBillingConnection.FromSettings(new PlayBillingSettings
        {
            PackageName = packageName, ServiceAccountJson = ServiceAccountJson(), Products = DefaultProducts
        });

        Assert.False(connection.IsEnabled);
        Assert.Contains(packageName, connection.DisabledReason);
    }

    private static PlayBillingConnection FromConfiguration(Dictionary<string, string?> values)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(values).Build();
        return new ServiceCollection().AddPlayBilling(configuration).BuildServiceProvider().GetRequiredService<PlayBillingConnection>();
    }

    [Fact]
    public void Settings_are_read_from_configuration()
    {
        var connection = FromConfiguration(new Dictionary<string, string?>
        {
            // appsettings.json
            ["PlayBilling:PackageName"] = "com.sardnovels.app",
            ["PlayBilling:Products:points_500"] = "500",
            ["PlayBilling:Products:points_1000"] = "1000",
            // host configuration (PlayBilling__ServiceAccountJson etc.)
            ["PlayBilling:ServiceAccountJson"] = Convert.ToBase64String(Encoding.UTF8.GetBytes(ServiceAccountJson())),
            ["PlayBilling:Products:points_1000"] = "1200",
            ["PlayBilling:AllowTestPurchases"] = "true"
        });

        Assert.True(connection.IsEnabled, connection.DisabledReason);
        Assert.True(connection.AllowTestPurchases);
        Assert.Equal([("points_500", 500), ("points_1000", 1200)], connection.Products.Select(p => (p.ProductId, p.Points)));
    }

    [Fact]
    public void A_typo_in_the_points_is_reported_not_dropped_silently()
    {
        var connection = FromConfiguration(new Dictionary<string, string?>
        {
            ["PlayBilling:ServiceAccountJson"] = ServiceAccountJson(),
            ["PlayBilling:Products:points_500"] = "five hundred",
            ["PlayBilling:Products:points_1000"] = "1000"
        });

        Assert.True(connection.IsEnabled, connection.DisabledReason);
        Assert.Equal(["points_1000"], connection.Products.Select(p => p.ProductId));
        Assert.Equal(["points_500=five hundred"], connection.IgnoredProducts);
    }

    [Fact]
    public void A_typo_in_the_test_purchase_switch_never_lets_test_purchases_in_and_never_stops_the_API()
    {
        var connection = FromConfiguration(new Dictionary<string, string?>
        {
            ["PlayBilling:ServiceAccountJson"] = ServiceAccountJson(),
            ["PlayBilling:Products:points_1000"] = "1000",
            ["PlayBilling:AllowTestPurchases"] = "yes please"
        });

        Assert.False(connection.AllowTestPurchases);
    }

    [Fact]
    public void The_account_id_is_the_lowercase_hex_SHA256_of_the_user_id()
    {
        // Known vector: SHA-256("abc").
        Assert.Equal("ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad", PlayAccountId.For("abc"));

        var userId = Guid.NewGuid().ToString();
        var id = PlayAccountId.For(userId);
        Assert.Matches("^[0-9a-f]{64}$", id); // Play's limit for obfuscatedAccountId is 64 characters
        Assert.Equal(Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(userId))).ToLowerInvariant(), id);

        Assert.True(PlayAccountId.Matches(id, userId));
        Assert.True(PlayAccountId.Matches(id.ToUpperInvariant(), userId));
        Assert.False(PlayAccountId.Matches(PlayAccountId.For("someone-else"), userId));
        Assert.False(PlayAccountId.Matches(null, userId));
        Assert.False(PlayAccountId.Matches(userId, userId));
    }
}
