using Application.Gifts;
using Application.Posts;

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

    /// <summary>
    /// What a new post accepts (#43), for the apps' counter and picture checks: the limits the server checks posts
    /// against (<see cref="PostRules"/>). Not settings: the handler always serves the rules themselves.
    /// </summary>
    public PostsAppConfigDto Posts { get; set; } = new();
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

public class PostsAppConfigDto
{
    /// <summary>
    /// The longest text, in user-perceived characters (an emoji is one), counted after trimming; longer is
    /// PostContentTooLong.
    /// </summary>
    public int ContentMaxLength { get; set; } = PostRules.ContentMaxLength;

    /// <summary>The largest picture, in bytes (5 MB: 5,242,880); larger is PostImageTooLarge.</summary>
    public long ImageMaxBytes { get; set; } = PostRules.ImageMaxBytes;

    /// <summary>The picture types, as the file's Content-Type; another is PostImageType.</summary>
    public IReadOnlyList<string> ImageTypes { get; set; } = PostRules.ImageTypes;
}

public class MaintenanceConfigDto
{
    public bool Enabled { get; set; }

    /// <summary>The Arabic message to show while <see cref="Enabled"/>; null for the app's own default.</summary>
    public string? MessageAr { get; set; }
}
