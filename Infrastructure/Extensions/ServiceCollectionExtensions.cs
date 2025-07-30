using Domain.Entities;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.AspNetCore.Identity;
using Domain.Repositories;
using Infrastructure.Repositories;
using Infrastructure.Configuration;
using Amazon.S3;
using Application.Services;
using Infrastructure.Services;
using Amazon.Runtime;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using System.Text;
using Infrastructure.Authorization;
using Application.Users;
using Microsoft.AspNetCore.Authentication.Google;
using Microsoft.AspNetCore.Authentication;
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
        })
        .AddEntityFrameworkStores<ApplicationDbContext>()
        .AddClaimsPrincipalFactory<SardUserClaimsPrincipalFactory>()
        .AddDefaultTokenProviders();

        services.AddScoped<IUsersRepository, UsersRepositories>();
        services.AddScoped<INovelsRepository, NovelsRepository>();


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
