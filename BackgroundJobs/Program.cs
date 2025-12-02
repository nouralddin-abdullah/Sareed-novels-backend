using Microsoft.Azure.Functions.Worker;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Infrastructure.Persistence;
using Infrastructure.Repositories;
using Application.Services;
using Infrastructure.Services;
using Domain.Repositories;
using Domain.Entities;
using Microsoft.AspNetCore.Identity;

var host = new HostBuilder()
    .ConfigureFunctionsWorkerDefaults()
    .ConfigureServices((context, services) =>
    {
        var configuration = context.Configuration;

        services.AddApplicationInsightsTelemetryWorkerService();

        services.AddDbContext<ApplicationDbContext>(options =>
            options.UseSqlServer(
                configuration.GetConnectionString("Default"),
                b => b.MigrationsAssembly("Infrastructure")));

        // Add Data Protection (required by Identity)
        services.AddDataProtection();

        // Required for WalletService (even though daily unlock doesn't use it, PrivilegeService constructor requires it)
        services.AddIdentityCore<User>()
            .AddEntityFrameworkStores<ApplicationDbContext>()
            .AddDefaultTokenProviders();

        // Background Job 1: Ranking Recalculation
        services.AddScoped<IRankingService, RankingService>();
        
        // Background Job 2: Daily Chapter Unlock
        services.AddScoped<IPrivilegeService, PrivilegeService>();
        services.AddScoped<INovelPrivilegeRepository, NovelPrivilegeRepository>();
        services.AddScoped<IPrivilegeSubscriptionRepository, PrivilegeSubscriptionRepository>();
        services.AddScoped<INovelsRepository, NovelsRepository>();
        services.AddScoped<IChaptersRepository, ChaptersRepository>();
        
        // These are required by PrivilegeService constructor, but NOT used by PerformDailyUnlockAsync
        services.AddScoped<IWalletService, WalletService>();
        services.AddScoped<INotificationService, NotificationService>();
        services.AddScoped<IUsersRepository, UsersRepositories>();
        services.AddScoped<ITransactionManager, TransactionManager>();
        services.AddScoped<IUserWalletRepository, UserWalletRepository>();
        services.AddScoped<IPointTransactionRepository, PointTransactionRepository>();
        services.AddScoped<IPointCalculationService, PointCalculationService>();
        services.AddScoped<INotificationsRepository, NotificationsRepository>();
    })
    .Build();

await host.RunAsync();
