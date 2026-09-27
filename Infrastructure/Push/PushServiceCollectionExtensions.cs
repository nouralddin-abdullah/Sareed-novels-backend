using System.Net;
using Domain.Repositories;
using Infrastructure.BackgroundJobs;
using Infrastructure.Configuration;
using Infrastructure.Repositories;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Infrastructure.Push;

public static class PushServiceCollectionExtensions
{
    /// <summary>Device tokens, notification preferences, and FCM push delivery through the outbox worker.</summary>
    public static IServiceCollection AddPushNotifications(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddScoped<IUserDevicesRepository, UserDevicesRepository>();
        services.AddScoped<INotificationPreferencesRepository, NotificationPreferencesRepository>();

        // Read once at startup: the credentials, or why push is disabled.
        var settings = configuration.GetSection(FcmSettings.SectionName).Get<FcmSettings>() ?? new FcmSettings();
        services.AddSingleton(FcmConnection.FromSettings(settings));

        services.AddOptions<PushDeliveryOptions>();
        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton<PushOutboxSignal>();
        services.AddHttpClient<IPushService, FcmPushService>(client =>
        {
            client.BaseAddress = FcmPushService.BaseAddress;
            client.Timeout = TimeSpan.FromSeconds(15);
            // FCM recommends HTTP/2 (many requests on one connection); falls back to HTTP/1.1.
            client.DefaultRequestVersion = HttpVersion.Version20;
            client.DefaultVersionPolicy = HttpVersionPolicy.RequestVersionOrLower;
        });
        services.AddScoped<PushTargetResolver>();
        services.AddScoped<PushOutboxProcessor>();
        services.AddHostedService<PushNotificationWorker>();
        return services;
    }
}
