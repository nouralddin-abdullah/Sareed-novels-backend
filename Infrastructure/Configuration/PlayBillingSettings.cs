namespace Infrastructure.Configuration;

/// <summary>
/// Google Play Billing for point packs (section <c>PlayBilling</c>). The service-account key comes from host
/// configuration only (environment variable <c>PlayBilling__ServiceAccountJson</c>), never from appsettings files.
/// Without it the app can't sell packs: the catalog says so and purchases answer 503. See README.md.
/// </summary>
public class PlayBillingSettings
{
    public const string SectionName = "PlayBilling";

    /// <summary>The Android app's package name. Defaults to com.sardnovels.app.</summary>
    public string? PackageName { get; set; }

    /// <summary>
    /// The content of the Google Cloud service account's JSON key, as is or base64-encoded (easier to paste where
    /// quotes need escaping). The account needs access to the app in Play Console.
    /// </summary>
    public string? ServiceAccountJson { get; set; }

    /// <summary>
    /// Play product id → points it credits. A value of 0 takes a product out of the catalog. Read as text so that a value
    /// that isn't a number is reported at startup (the configuration binder would drop it silently).
    /// </summary>
    public Dictionary<string, string?> Products { get; set; } = new();

    /// <summary>
    /// Credit purchases made by Play Console license testers (Play's purchaseType 0, not charged). Off by default: on in
    /// production, testers get real points for free. Turn on only while testing, then off again.
    /// </summary>
    public bool AllowTestPurchases { get; set; }
}
