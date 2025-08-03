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


        //adding cloudflare settings
        services.Configure<CloudflareR2Settings>(
            configuration.GetSection(CloudflareR2Settings.SectionName));

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



        //email sender
        services.AddSingleton<IEmailSender>(provider =>
        new SendGridEmailSender(
            apiKey: "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",
            fromEmail: "sardconfirmation@protonmail.com",
            fromName: "SardConfirmation"
        ));

        services.AddScoped<IJWTService, JwtService>();
    }
}
