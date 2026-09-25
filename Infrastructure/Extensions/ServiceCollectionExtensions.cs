using Amazon.Runtime;
using Amazon.S3;
using Application.Services;
using Domain.Entities;
using Domain.Repositories;
using Infrastructure.Authorization;
using Infrastructure.Configuration;
using Infrastructure.Persistence;
using Infrastructure.Repositories;
using Infrastructure.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Infrastructure.BackgroundJobs;
using Infrastructure.Services.Search;
using Infrastructure.Services.Covers;

namespace Infrastructure.Extensions;

public static class ServiceCollectionExtensions
{
    public static void AddInfrastructure(this IServiceCollection services, IConfiguration configuration, bool isDevelopment = false)
    {
        var ConnectionString = configuration.GetConnectionString("SardDb");
        services.AddDbContext<ApplicationDbContext>(options =>
        {
            options.UseSqlServer(ConnectionString);
            // Logs SQL parameter values (user data); never in production.
            if (isDevelopment)
            {
                options.EnableSensitiveDataLogging();
            }
        });

        services.AddIdentity<User, IdentityRole>(options =>
        {
            options.User.RequireUniqueEmail = true;

            // Password options - Make them more user-friendly
            options.Password.RequireDigit = false;              // Don't require numbers
            options.Password.RequireLowercase = false;          // Don't require lowercase
            options.Password.RequireUppercase = false;          // Don't require uppercase
            options.Password.RequireNonAlphanumeric = false;    // Don't require special characters
            options.Password.RequiredLength = 6;                // Minimum 6 characters
            options.Password.RequiredUniqueChars = 0;           // At least 0 unique character

            // Sign-in lockout (UserLoginCommandHandler): 5 wrong passwords lock the account for 5 minutes.
            options.Lockout.AllowedForNewUsers = true;
            options.Lockout.MaxFailedAccessAttempts = 5;
            options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(5);
        })
        .AddEntityFrameworkStores<ApplicationDbContext>()
        .AddClaimsPrincipalFactory<SardUserClaimsPrincipalFactory>()
        .AddDefaultTokenProviders();

        services.AddScoped<IUsersRepository, UsersRepositories>();
        services.AddScoped<INovelsRepository, NovelsRepository>();
        services.AddScoped<IReviewsRepository, ReviewsRepository>();
        services.AddScoped<IReviewLikesRepository, ReviewLikesRepository>();
        services.AddScoped<INovelGenresRepository, NovelGenresRepository>();
        services.AddScoped<IGenresRepository, GenresRepository>();
        services.AddScoped<IViewTrackingService, ViewTrackingService>();
        services.AddScoped<IRankingService, RankingService>();
        services.AddScoped<IRankingRepository, RankingRepository>();
        services.AddScoped<IChaptersRepository, ChaptersRepository>();
        services.AddScoped<ICommentsRepository, CommentsRepository>();
        services.AddScoped<ICommentLikesRepository, CommentLikesRepository>();
        services.AddScoped<IChapterParagraphsRepository, ChapterParagraphsRepository>();
        services.AddScoped<IReadingListsRepository, ReadingListsRepository>();
        services.AddScoped<IReadingListNovelsRepository, ReadingListNovelsRepository>();
        services.AddScoped<IReadingListFollowersRepository, ReadingListFollowersRepository>();
        services.AddScoped<ILibraryRepository, LibraryRepository>();
        services.AddScoped<IChapterSequenceService, ChapterSequenceService>();
        services.AddScoped<IPostsRepository, PostsRepository>();
        services.AddScoped<IPostLikesRepository, PostLikesRepository>();
        services.AddScoped<INovelEntityRepository, NovelEntityRepository>();
        services.AddScoped<INotificationsRepository, NotificationsRepository>();
        services.AddScoped<INotificationService, NotificationService>();
        services.AddScoped<ITransactionManager, TransactionManager>();
        
        // Wallet System
        services.AddScoped<IUserWalletRepository, UserWalletRepository>();
        services.AddScoped<IRechargeRequestRepository, RechargeRequestRepository>();
        services.AddScoped<IWithdrawalRequestRepository, WithdrawalRequestRepository>();
        services.AddScoped<IPointTransactionRepository, PointTransactionRepository>();
        services.AddScoped<IPointCalculationService, PointCalculationService>();
        services.AddScoped<IWalletService, WalletService>();
        
        // Gift System
        services.AddScoped<IGiftRepository, GiftRepository>();
        services.AddScoped<IGiftTransactionRepository, GiftTransactionRepository>();
        services.AddScoped<IGlobalSupporterLeaderboardRepository, GlobalSupporterLeaderboardRepository>();
        
        // Privilege System
        services.AddScoped<INovelPrivilegeRepository, NovelPrivilegeRepository>();
        services.AddScoped<IPrivilegeSubscriptionRepository, PrivilegeSubscriptionRepository>();
        services.AddScoped<IPrivilegeService, PrivilegeService>();
        
        // Competition System
        services.AddScoped<ICompetitionRepository, CompetitionRepository>();
        services.AddScoped<ICompetitionParticipantRepository, CompetitionParticipantRepository>();
        services.AddScoped<ICompetitionWinnerRepository, CompetitionWinnerRepository>();

        //adding cloudflare settings
        services.Configure<CloudflareR2Settings>(
            configuration.GetSection(CloudflareR2Settings.SectionName));

        // Search runs on SQL Server (normalized search columns); there is no index to keep in sync.
        services.AddScoped<INovelSearchService, NovelSearchService>();
        services.AddScoped<IUserSearchService, UserSearchService>();
        services.AddScoped<IEntitySearchService, EntitySearchService>();
        services.AddScoped<INovelRecommendationService, NovelRecommendationService>();

        // Scheduled work runs inside the API (replaces the retired Azure Functions app).
        services.TryAddSingleton(TimeProvider.System);
        services.AddHostedService<RankingRecalculationService>();
        services.AddHostedService<DailyPrivilegeUnlockService>();
        services.AddHostedService<GiftLeaderboardRecalculationService>();

        // Configure memory cache for recommendations
        services.AddMemoryCache(options =>
        {
            options.SizeLimit = 10000;           // Max 10,000 cache entries
            options.CompactionPercentage = 0.20; // Remove 20% oldest when full
            options.ExpirationScanFrequency = TimeSpan.FromHours(1); // Cleanup hourly
        });


        //adding R2 S3 Client 
        services.AddSingleton<IAmazonS3>(provider =>
        {
            var settings = configuration.GetSection(CloudflareR2Settings.SectionName).Get<CloudflareR2Settings>();

            // ServiceUrl is only set to point at an S3-compatible stand-in (local runs, tests); production uses R2.
            var config = string.IsNullOrWhiteSpace(settings!.ServiceUrl)
                ? new AmazonS3Config { ServiceURL = "https://1700ebc57525e0a0f6a5ff6f27d93218.r2.cloudflarestorage.com" }
                : new AmazonS3Config { ServiceURL = settings.ServiceUrl, ForcePathStyle = true };

            var credentials = new BasicAWSCredentials(settings.AccessKey, settings.SecretKey);
            return new AmazonS3Client(credentials, config);
        });

        services.AddScoped<IFileUploadService, CloudflareR2Service>();
        services.AddScoped<IObjectStorage, CloudflareR2Service>();

        // Novel covers: normalized to the 2:3 WebP standard on upload (Application.Covers.NovelCovers).
        services.AddScoped<INovelCoverService, NovelCoverService>();

        // Configure SMTP settings
        services.Configure<SmtpSettings>(
            configuration.GetSection(SmtpSettings.SectionName));

        // Email sender using Hostinger SMTP
        services.AddScoped<IEmailSender>(provider =>
        {
            var smtpSettings = configuration.GetSection(SmtpSettings.SectionName).Get<SmtpSettings>();
            var logger = provider.GetRequiredService<ILogger<SmtpEmailSender>>();
            
            if (smtpSettings == null)
            {
                throw new InvalidOperationException(
                    "SMTP settings are not configured properly in appsettings.json");
            }

            return new SmtpEmailSender(
                smtpHost: smtpSettings.Host,
                smtpPort: smtpSettings.Port,
                fromEmail: smtpSettings.FromEmail,
                fromName: smtpSettings.FromName,
                username: smtpSettings.Username,
                password: smtpSettings.Password,
                logger: logger
            );
        });

        services.AddScoped<IJWTService, JwtService>();
    }
}
