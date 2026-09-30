using System.Text.RegularExpressions;
using Application.Gifts;
using MediatR;
using Microsoft.Extensions.Configuration;

namespace Application.AppConfig.Queries.GetAppConfig;

/// <summary>
/// Reads the "AppConfig" section on every request, so a changed value (appsettings or environment) applies without a
/// deploy. A version that isn't major.minor.patch, or a minimum above the latest, is a configuration mistake: it fails
/// loudly (logged 500) rather than telling every installed app something wrong. So is a gift message limit outside
/// 1 to 1000.
/// </summary>
public partial class GetAppConfigQueryHandler(IConfiguration configuration) : IRequestHandler<GetAppConfigQuery, AppConfigDto>
{
    public const string Section = "AppConfig";

    public Task<AppConfigDto> Handle(GetAppConfigQuery request, CancellationToken cancellationToken)
    {
        var config = configuration.GetSection(Section).Get<AppConfigDto>() ?? new AppConfigDto();

        CheckVersions("Android", config.Android.MinVersion, config.Android.LatestVersion);
        CheckVersions("Ios", config.Ios.MinVersion, config.Ios.LatestVersion);

        if (string.IsNullOrWhiteSpace(config.Maintenance.MessageAr))
        {
            config.Maintenance.MessageAr = null;
        }

        // The very number SendGift checks messages against (#31), which also refuses a value out of range.
        config.Gifts.MessageMaxLength = GiftMessageRules.MaxLength(configuration);

        // The post rules themselves (#43), whatever an "AppConfig:Posts" section might say.
        config.Posts = new PostsAppConfigDto();

        return Task.FromResult(config);
    }

    private static void CheckVersions(string platform, string minVersion, string latestVersion)
    {
        var min = ParseVersion(minVersion, $"{platform}:MinVersion");
        var latest = ParseVersion(latestVersion, $"{platform}:LatestVersion");
        if (min > latest)
        {
            throw new InvalidOperationException(
                $"{Section}:{platform}:MinVersion ({minVersion}) is above {Section}:{platform}:LatestVersion ({latestVersion})");
        }
    }

    private static Version ParseVersion(string? value, string key) =>
        value != null && SemanticVersionCore().IsMatch(value)
            ? Version.Parse(value)
            : throw new InvalidOperationException($"{Section}:{key} must look like 1.2.3, not '{value}'");

    [GeneratedRegex(@"^\d{1,9}\.\d{1,9}\.\d{1,9}$")]
    private static partial Regex SemanticVersionCore();
}
