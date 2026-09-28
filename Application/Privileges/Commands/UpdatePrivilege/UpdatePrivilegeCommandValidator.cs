using FluentValidation;

namespace Application.Privileges.Commands.UpdatePrivilege;

public class UpdatePrivilegeCommandValidator : AbstractValidator<UpdatePrivilegeCommand>
{
    public UpdatePrivilegeCommandValidator()
    {
        RuleFor(x => x.NovelId)
            .NotEmpty()
            .WithMessage("حدد الرواية");
        
        When(x => x.NewSubscriptionCost.HasValue, () =>
        {
            RuleFor(x => x.NewSubscriptionCost!.Value)
                .InclusiveBetween(100, 2000)
                .WithMessage("سعر الاشتراك يجب أن يكون من 100 إلى 2000 نقطة");
        });
        
        When(x => x.NewPrivilegeStartSequence.HasValue, () =>
        {
            RuleFor(x => x.NewPrivilegeStartSequence!.Value)
                .GreaterThanOrEqualTo(11)
                .WithMessage("تبقى الفصول العشرة الأولى مجانية للقرّاء، فالوصول المبكر يبدأ من الفصل 11 أو بعده.");
        });
    }
}
