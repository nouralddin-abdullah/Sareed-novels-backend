using Application.Validation;
using FluentValidation;

namespace Application.Entities.Commands.CreateEntity;

public class CreateEntityCommandValidator : AbstractValidator<CreateEntityCommand>
{
    public CreateEntityCommandValidator()
    {
        RuleFor(x => x.Section)
            .NotEmpty()
            .WithMessage("اختر قسم المدخل")
            .MaximumLength(50)
            .WithMessage("يجب ألا يتجاوز اسم القسم 50 حرفًا");

        RuleFor(x => x.Name)
            .NotEmpty()
            .WithMessage("اكتب اسم المدخل")
            .MaximumLength(200)
            .WithMessage("يجب ألا يتجاوز اسم المدخل 200 حرف");

        RuleFor(x => x.ShortDescription)
            .MaximumLength(500)
            .When(x => x.ShortDescription != null)
            .WithMessage("يجب ألا يتجاوز الوصف المختصر 500 حرف");

        RuleFor(x => x.Description)
            .MaximumLength(5000)
            .When(x => x.Description != null)
            .WithMessage("يجب ألا يتجاوز الوصف 5000 حرف");

        RuleFor(x => x.Role)
            .MaximumLength(100)
            .When(x => x.Role != null)
            .WithMessage("يجب ألا يتجاوز الدور 100 حرف");

        RuleFor(x => x.ImageFile)
            .Must(ImageValidationUtils.IsValidImageFile)
            .When(x => x.ImageFile != null)
            .WithMessage("صورة المدخل يجب أن تكون بصيغة JPEG أو PNG أو WebP، وحجمها 5 ميغابايت أو أقل");
    }
}
