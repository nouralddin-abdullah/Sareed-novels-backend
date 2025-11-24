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
using Microsoft.Extensions.Logging;
using OpenSearch.Client; // ✅ Changed from Nest
using OpenSearch.Net; // ✅ Changed from Elasticsearch.Net

namespace Infrastructure.Extensions;

public static class ServiceCollectionExtensions
{
    public static void AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        var ConnectionString = configuration.GetConnectionString("SardDb");
        services.AddDbContext<ApplicationDbContext>(Options => Options
            .UseSqlServer(ConnectionString)
            .EnableSensitiveDataLogging());

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
        services.AddScoped<ISearchIndexOutboxRepository, SearchIndexOutboxRepository>();
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

        //adding cloudflare settings
        services.Configure<CloudflareR2Settings>(
            configuration.GetSection(CloudflareR2Settings.SectionName));

        // OpenSearch configuration
        services.Configure<OpenSearchSettings>(
            configuration.GetSection(OpenSearchSettings.SectionName));

        // Register OpenSearch client (using official OpenSearch.Client)
        services.AddSingleton<IOpenSearchClient>(provider =>
        {
            var settings = configuration.GetSection(OpenSearchSettings.SectionName)
                .Get<OpenSearchSettings>();

            if (settings == null || string.IsNullOrEmpty(settings.Url))
            {
                throw new InvalidOperationException(
                    "OpenSearch settings are not configured properly in appsettings.json");
            }

            // Create connection settings for AWS OpenSearch
            var uri = new Uri($"https://{settings.Url}");
            
            var connectionSettings = new ConnectionSettings(uri)
                .DefaultIndex(settings.NovelIndexName)
                .BasicAuthentication(settings.Username, settings.Password)
                .EnableDebugMode()
                .PrettyJson()
                .ServerCertificateValidationCallback((o, cert, chain, errors) => true)
                .RequestTimeout(TimeSpan.FromMinutes(2));

            return new OpenSearchClient(connectionSettings);
        });

        // Register search services
        services.AddScoped<INovelSearchService, NovelSearchService>();
        services.AddScoped<IUserSearchService, UserSearchService>();
        services.AddScoped<IEntitySearchService, EntitySearchService>();
        services.AddScoped<ISearchIndexQueueService, SearchIndexQueueService>();

        // Register background service for outbox processing
        services.AddHostedService<SearchIndexSyncService>();

        //adding R2 S3 Client 
        services.AddSingleton<IAmazonS3>(provider =>
        {
            var settings = configuration.GetSection(CloudflareR2Settings.SectionName).Get<CloudflareR2Settings>();

            var config = new AmazonS3Config
            {
                ServiceURL = $"https://1700ebc57525e0a0f6a5ff6f27d93218.r2.cloudflarestorage.com"
            };

            var credentials = new BasicAWSCredentials(settings!.AccessKey, settings.SecretKey);
            return new AmazonS3Client(credentials, config);
        });

        services.AddScoped<IFileUploadService, CloudflareR2Service>();

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
