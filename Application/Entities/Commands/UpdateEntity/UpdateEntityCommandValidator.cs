using Application.Validation;
using FluentValidation;
using Microsoft.AspNetCore.Http;

namespace Application.Entities.Commands.UpdateEntity;

public class UpdateEntityCommandValidator : AbstractValidator<UpdateEntityCommand>
{
    public UpdateEntityCommandValidator()
    {
        RuleFor(x => x.Section)
            .MaximumLength(50)
            .When(x => !string.IsNullOrEmpty(x.Section))
            .WithMessage("يجب ألا يتجاوز اسم القسم 50 حرفًا");

        RuleFor(x => x.Name)
            .MaximumLength(200)
            .When(x => !string.IsNullOrEmpty(x.Name))
            .WithMessage("يجب ألا يتجاوز اسم المدخل 200 حرف");

        RuleFor(x => x.ShortDescription)
            .MaximumLength(500)
            .When(x => !string.IsNullOrEmpty(x.ShortDescription))
            .WithMessage("يجب ألا يتجاوز الوصف المختصر 500 حرف");

        RuleFor(x => x.Description)
            .MaximumLength(5000)
            .When(x => !string.IsNullOrEmpty(x.Description))
            .WithMessage("يجب ألا يتجاوز الوصف 5000 حرف");

        RuleFor(x => x.Role)
            .MaximumLength(100)
            .When(x => !string.IsNullOrEmpty(x.Role))
            .WithMessage("يجب ألا يتجاوز الدور 100 حرف");

        RuleFor(x => x.ImageFile)
            .Must(file => ImageValidationUtils.IsValidImageFile(file))
            .When(x => x.ImageFile != null, ApplyConditionTo.CurrentValidator)
            .WithMessage("صورة المدخل يجب أن تكون بصيغة JPEG أو PNG أو WebP، وحجمها 5 ميغابايت أو أقل");
    }
}
