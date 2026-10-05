using Application.Chapters.Scheduling;
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
        services.AddMediatR(cfg =>
        {
            cfg.RegisterServicesFromAssembly(applicationAssembly);
            // Reading a novel or its chapters publishes its due scheduled chapters first (#77).
            cfg.AddOpenBehavior(typeof(PublishDueChaptersBehavior<,>));
        });
        services.AddAutoMapper(applicationAssembly);
        services.AddValidatorsFromAssembly(applicationAssembly)
            .AddFluentValidationAutoValidation();

        services.AddScoped<IUserContext, UserContext>();
        services.AddScoped<IVisitorContext, VisitorContext>();
        services.AddHttpContextAccessor();

        // The per-account limit on deleting one's account (counts live for the whole process).
        services.AddSingleton<AccountDeletionAttempts>();

        // Proving it's them again: deleting the account, setting its first password.
        services.AddScoped<Reauthentication>();

        // update-me's checks on a new user name, for GET username-available and Google sign-up's handle (#69).
        services.AddScoped<UserNameCheck>();
        services.AddScoped<GoogleUserNames>();

        // Scheduled chapters (#77): published by the scheduler (Infrastructure) and before reads of their novel.
        services.AddScoped<ScheduledChapterPublisher>();
    }
}
