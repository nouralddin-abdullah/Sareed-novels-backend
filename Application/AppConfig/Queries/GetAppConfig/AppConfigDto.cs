using Application.Gifts;

namespace Application.AppConfig.Queries.GetAppConfig;

/// <summary>
/// What the mobile app checks at startup: whether its version is still supported, and whether the service is down for
/// maintenance. Bound from the "AppConfig" configuration section.
/// </summary>
public class AppConfigDto
{
    public AndroidAppConfigDto Android { get; set; } = new();

    /// <summary>The same for the iOS app (#25), ready before its first release.</summary>
    public IosAppConfigDto Ios { get; set; } = new();

    public MaintenanceConfigDto Maintenance { get; set; } = new();

    /// <summary>What gifts accept (#31). An older server has no such section: then the apps offer no message box.</summary>
    public GiftsAppConfigDto Gifts { get; set; } = new();
}

public class AndroidAppConfigDto
{
    /// <summary>The oldest app version (major.minor.patch) that still works with the API; older builds must update.</summary>
    public string MinVersion { get; set; } = "1.0.0";

    /// <summary>The newest version on Google Play, so the app can suggest (not force) an update.</summary>
    public string LatestVersion { get; set; } = "1.0.0";
}

public class IosAppConfigDto
{
    /// <summary>The oldest app version (major.minor.patch) that still works with the API; older builds must update.</summary>
    public string MinVersion { get; set; } = "1.0.0";

    /// <summary>The newest version on the App Store, so the app can suggest (not force) an update.</summary>
    public string LatestVersion { get; set; } = "1.0.0";
}

public class GiftsAppConfigDto
{
    /// <summary>
    /// The longest message a gift can carry, in user-perceived characters (an emoji is one), from
    /// AppConfig:Gifts:MessageMaxLength; the server refuses longer ones (GiftMessageTooLong) by the same number.
    /// </summary>
    public int MessageMaxLength { get; set; } = GiftMessageRules.DefaultMaxLength;
}

public class MaintenanceConfigDto
{
    public bool Enabled { get; set; }

    /// <summary>The Arabic message to show while <see cref="Enabled"/>; null for the app's own default.</summary>
    public string? MessageAr { get; set; }
}
