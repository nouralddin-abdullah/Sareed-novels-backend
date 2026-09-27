using Application.Services;
using Domain.Repositories;
using Infrastructure.BackgroundJobs;
using Infrastructure.Configuration;
using Infrastructure.Repositories;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Infrastructure.PlayBilling;

public static class PlayBillingServiceCollectionExtensions
{
    /// <summary>Point packs sold through Google Play Billing: verification, consumption and voided-purchase handling.</summary>
    public static IServiceCollection AddPlayBilling(this IServiceCollection services, IConfiguration configuration)
    {
        // Read once at startup: the catalog and credentials, or why billing is disabled (logged by the worker at startup).
        services.AddSingleton(ReadConnection(configuration));
        services.TryAddSingleton(TimeProvider.System);
        services.AddHttpClient<GooglePlayApi>(client =>
        {
            client.BaseAddress = GooglePlayApi.BaseAddress;
            client.Timeout = TimeSpan.FromSeconds(20);
        });
        services.AddScoped<IPlayPurchaseRepository, PlayPurchaseRepository>();
        services.AddScoped<PlayBillingService>();
        services.AddScoped<IPlayBillingService>(provider => provider.GetRequiredService<PlayBillingService>());
        services.AddHostedService<PlayBillingWorker>();
        return services;
    }

    private static PlayBillingConnection ReadConnection(IConfiguration configuration)
    {
        PlayBillingSettings settings;
        try
        {
            settings = configuration.GetSection(PlayBillingSettings.SectionName).Get<PlayBillingSettings>() ?? new PlayBillingSettings();
        }
        catch (InvalidOperationException ex)
        {
            // A typo in a value (points that aren't a number) disables billing instead of stopping the whole API.
            return PlayBillingConnection.Disabled($"the PlayBilling settings can't be read: {ex.Message}");
        }
        return PlayBillingConnection.FromSettings(settings);
    }
}
