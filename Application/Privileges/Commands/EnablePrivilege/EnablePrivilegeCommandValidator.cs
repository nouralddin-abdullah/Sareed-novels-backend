using FluentValidation;

namespace Application.Privileges.Commands.EnablePrivilege;

public class EnablePrivilegeCommandValidator : AbstractValidator<EnablePrivilegeCommand>
{
    public EnablePrivilegeCommandValidator()
    {
        RuleFor(x => x.NovelId)
            .NotEmpty()
            .WithMessage("حدد الرواية");
        
        RuleFor(x => x.SubscriptionCost)
            .InclusiveBetween(100, 2000)
            .WithMessage("سعر الاشتراك يجب أن يكون من 100 إلى 2000 نقطة");
        
        When(x => x.PrivilegeStartSequence.HasValue, () =>
        {
            RuleFor(x => x.PrivilegeStartSequence!.Value)
                .GreaterThanOrEqualTo(11)
                .WithMessage("تبقى الفصول العشرة الأولى مجانية للقرّاء، فالوصول المبكر يبدأ من الفصل 11 أو بعده.");
        });
    }
}
