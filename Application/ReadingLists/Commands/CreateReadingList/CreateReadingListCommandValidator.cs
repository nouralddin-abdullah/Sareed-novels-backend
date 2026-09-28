using Application.Validation;
using FluentValidation;

namespace Application.ReadingLists.Commands.CreateReadingList;

public class CreateReadingListCommandValidator : AbstractValidator<CreateReadingListCommand>
{
    public CreateReadingListCommandValidator()
    {
        RuleFor(x => x.Name)
            .NotEmpty()
            .WithMessage("اكتب اسم القائمة")
            .Length(1, 100)
            .WithMessage("يجب أن يكون اسم القائمة من 1 إلى 100 حرف");

        RuleFor(x => x.Description)
            .MaximumLength(1000)
            .WithMessage("يجب ألا يتجاوز وصف القائمة 1000 حرف")
            .When(x => !string.IsNullOrWhiteSpace(x.Description));

        // Validate image file if provided
        RuleFor(x => x.CoverImage)
            .Must(ImageValidationUtils.IsValidImageFile)
            .WithMessage("صورة القائمة يجب أن تكون بصيغة JPEG أو PNG أو WebP، وحجمها 5 ميغابايت أو أقل")
            .When(x => x.CoverImage != null);
    }
}
