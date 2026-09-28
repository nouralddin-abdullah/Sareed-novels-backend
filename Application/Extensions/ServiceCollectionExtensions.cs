using Application.Users;
using Application.Users.Commands.DeleteAccount;
using FluentValidation;
using FluentValidation.AspNetCore;
using Microsoft.Extensions.DependencyInjection;

namespace Application.Extensions;

public static class ServiceCollectionExtensions
{
    public static void AddApplication(this IServiceCollection services)
    {

        var applicationAssembly = typeof(ServiceCollectionExtensions).Assembly;
        services.AddMediatR(cfg => cfg.RegisterServicesFromAssembly(applicationAssembly));
        services.AddAutoMapper(applicationAssembly);
        services.AddValidatorsFromAssembly(applicationAssembly)
            .AddFluentValidationAutoValidation();

        services.AddScoped<IUserContext, UserContext>();
        services.AddScoped<IVisitorContext, VisitorContext>();
        services.AddHttpContextAccessor();

        // The per-account limit on deleting one's account (counts live for the whole process).
        services.AddSingleton<AccountDeletionAttempts>();
    }
}
