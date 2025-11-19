using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using Sareed_novels_backend.Middlewares;
using Serilog;

namespace Sareed_novels_backend.Extensions;

public static class WebApplicationBuilderExtensions
{
    public static void AddPresentation(this WebApplicationBuilder builder)
    {
        builder.Services.AddCors(options =>
        {
            // Main policy for authenticated requests (frontend with credentials)
            options.AddPolicy("AllowFrontend", policy =>
            {
                policy.WithOrigins(
                        // Development
                        "http://localhost:5173",
                        "https://localhost:5173",
                        "http://localhost:4173",
                        "https://localhost:4173",
                        // Production
                        "https://www.sardnovels.com",
                        "https://sardnovels.com",
                        // Cloudflare Pages preview/staging
                        "https://sard-frontend.pages.dev"
                    )
                    .AllowAnyHeader()
                    .AllowAnyMethod()
                    .AllowCredentials();
            });

            // Policy for Expo Go and mobile testing (TODO: Remove after mobile testing)
            options.AddPolicy("AllowMobile", policy =>
            {
                policy.SetIsOriginAllowed(origin =>
                {
                    // Allow localhost with any port (for Expo Metro bundler)
                    if (origin.StartsWith("http://localhost:") || origin.StartsWith("https://localhost:"))
                        return true;

                    // Allow Expo Go (exp:// protocol)
                    if (origin.StartsWith("exp://"))
                        return true;

                    // Allow local network IPs (for Expo Go on physical devices)
                    if (origin.StartsWith("http://192.168.") || 
                        origin.StartsWith("http://10.") || 
                        origin.StartsWith("http://172."))
                        return true;

                    return false;
                })
                .AllowAnyHeader()
                .AllowAnyMethod()
                .AllowCredentials();
            });

            // Separate policy for SEO bots and pre-rendering (read-only, no credentials)
            options.AddPolicy("AllowSEO", policy =>
            {
                policy.AllowAnyOrigin()  // Allow all origins for GET requests
                    .WithMethods("GET", "HEAD", "OPTIONS")  // Read-only methods
                    .AllowAnyHeader();
            });
        });

        // 1. Configure Authentication & JWT Bearer Validation
        builder.Services.AddAuthentication(options =>
        {
            options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
            options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
        })
        .AddJwtBearer(options =>
        {
            options.TokenValidationParameters = new TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidateAudience = true,
                ValidateLifetime = true,
                ValidateIssuerSigningKey = true,
                ValidIssuer = builder.Configuration["Jwt:Issuer"],
                ValidAudience = builder.Configuration["Jwt:Audience"],
                IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(builder.Configuration["Jwt:Key"]!))
            };
        });

        // 2. Add MVC Controllers
        builder.Services.AddControllers();

        // 3. Configure Swagger/OpenAPI
        builder.Services.AddEndpointsApiExplorer();

        builder.Services.AddSwaggerGen(c =>
        {
            // Define the "Bearer" security scheme
            c.AddSecurityDefinition("bearerAuth", new OpenApiSecurityScheme
            {
                Type = SecuritySchemeType.Http,
                Scheme = "Bearer"
            });

            // Make Swagger UI use the Bearer token
            c.AddSecurityRequirement(new OpenApiSecurityRequirement
            {
                {
                    new OpenApiSecurityScheme
                    {
                        Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = "bearerAuth" }
                    },
                    new List<string>()
                }
            });
        });

        // 4. Register Custom Middlewares
        builder.Services.AddScoped<ErrorHandlingMiddleware>();
        builder.Services.AddScoped<ExecutedTimeMiddleware>();

        // 5. Configure Serilog
        builder.Host.UseSerilog((context, configuration) =>
        {
            configuration.ReadFrom.Configuration(context.Configuration);
        });
    }
}