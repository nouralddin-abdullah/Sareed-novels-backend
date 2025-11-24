using FluentValidation;

namespace Application.Privileges.Commands.UpdatePrivilege;

public class UpdatePrivilegeCommandValidator : AbstractValidator<UpdatePrivilegeCommand>
{
    public UpdatePrivilegeCommandValidator()
    {
        RuleFor(x => x.NovelId)
            .NotEmpty()
            .WithMessage("Novel ID is required");
        
        When(x => x.NewSubscriptionCost.HasValue, () =>
        {
            RuleFor(x => x.NewSubscriptionCost!.Value)
                .InclusiveBetween(100, 2000)
                .WithMessage("Subscription cost must be between 100 and 2000 points");
        });
        
        When(x => x.NewPrivilegeStartSequence.HasValue, () =>
        {
            RuleFor(x => x.NewPrivilegeStartSequence!.Value)
                .GreaterThanOrEqualTo(11)
                .WithMessage("Privilege start sequence must be at least 11. The first 10 chapters must remain free for readers.");
        });
    }
}
