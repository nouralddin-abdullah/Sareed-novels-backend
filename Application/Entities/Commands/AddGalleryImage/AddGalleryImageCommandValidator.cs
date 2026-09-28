using Application.Validation;
using FluentValidation;

namespace Application.Entities.Commands.AddGalleryImage;

public class AddGalleryImageCommandValidator : AbstractValidator<AddGalleryImageCommand>
{
    public AddGalleryImageCommandValidator()
    {
        RuleFor(x => x.ImageFile)
            .NotNull()
            .WithMessage("اختر صورة")
            .Must(ImageValidationUtils.IsValidImageFile)
            .WithMessage("الصورة يجب أن تكون بصيغة JPEG أو PNG أو WebP، وحجمها 5 ميغابايت أو أقل");

        RuleFor(x => x.Caption)
            .MaximumLength(500)
            .When(x => x.Caption != null)
            .WithMessage("يجب ألا يتجاوز وصف الصورة 500 حرف");

        RuleFor(x => x.OrderIndex)
            .GreaterThanOrEqualTo(0)
            .WithMessage("ترتيب الصورة يجب أن يكون 0 أو أكثر");
    }
}
