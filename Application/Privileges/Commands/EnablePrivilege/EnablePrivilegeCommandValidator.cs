using FluentValidation;

namespace Application.Privileges.Commands.EnablePrivilege;

public class EnablePrivilegeCommandValidator : AbstractValidator<EnablePrivilegeCommand>
{
    public EnablePrivilegeCommandValidator()
    {
        RuleFor(x => x.NovelId)
            .NotEmpty()
            .WithMessage("Novel ID is required");
        
        RuleFor(x => x.SubscriptionCost)
            .InclusiveBetween(100, 2000)
            .WithMessage("Subscription cost must be between 100 and 2000 points");
        
        When(x => x.PrivilegeStartSequence.HasValue, () =>
        {
            RuleFor(x => x.PrivilegeStartSequence!.Value)
                .GreaterThanOrEqualTo(11)
                .WithMessage("Privilege start sequence must be at least 11. The first 10 chapters must remain free for readers.");
        });
    }
}
