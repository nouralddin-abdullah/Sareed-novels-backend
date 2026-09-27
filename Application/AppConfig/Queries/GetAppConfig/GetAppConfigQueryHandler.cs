using System.Text.RegularExpressions;
using MediatR;
using Microsoft.Extensions.Configuration;

namespace Application.AppConfig.Queries.GetAppConfig;

/// <summary>
/// Reads the "AppConfig" section on every request, so a changed value (appsettings or environment) applies without a
/// deploy. A version that isn't major.minor.patch, or a minimum above the latest, is a configuration mistake: it fails
/// loudly (logged 500) rather than telling every installed app something wrong.
/// </summary>
public partial class GetAppConfigQueryHandler(IConfiguration configuration) : IRequestHandler<GetAppConfigQuery, AppConfigDto>
{
    public const string Section = "AppConfig";

    public Task<AppConfigDto> Handle(GetAppConfigQuery request, CancellationToken cancellationToken)
    {
        var config = configuration.GetSection(Section).Get<AppConfigDto>() ?? new AppConfigDto();

        var min = ParseVersion(config.Android.MinVersion, "Android:MinVersion");
        var latest = ParseVersion(config.Android.LatestVersion, "Android:LatestVersion");
        if (min > latest)
        {
            throw new InvalidOperationException(
                $"{Section}:Android:MinVersion ({config.Android.MinVersion}) is above {Section}:Android:LatestVersion ({config.Android.LatestVersion})");
        }

        if (string.IsNullOrWhiteSpace(config.Maintenance.MessageAr))
        {
            config.Maintenance.MessageAr = null;
        }

        return Task.FromResult(config);
    }

    private static Version ParseVersion(string? value, string key) =>
        value != null && SemanticVersionCore().IsMatch(value)
            ? Version.Parse(value)
            : throw new InvalidOperationException($"{Section}:{key} must look like 1.2.3, not '{value}'");

    [GeneratedRegex(@"^\d{1,9}\.\d{1,9}\.\d{1,9}$")]
    private static partial Regex SemanticVersionCore();
}
