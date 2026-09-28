using Application.Validation;
using FluentValidation;

namespace Application.Comments.Commands.CreateComment;

public class CreateCommentValidator : AbstractValidator<CreateCommentRequest>
{
    public CreateCommentValidator()
    {
        RuleFor(x => x.Content)
            .NotEmpty()
            .WithMessage("اكتب تعليقك أولًا")
            .MaximumLength(2000)
            .WithMessage("التعليق يجب ألّا يتجاوز 2000 حرف")
            .MinimumLength(1)
            .WithMessage("اكتب تعليقك أولًا");

        // Validate image file if provided
        RuleFor(x => x.AttachedImage)
            .Must(ImageValidationUtils.IsValidImageFile)
            .WithMessage("الصورة يجب أن تكون بصيغة JPEG أو PNG أو WebP، وحجمها 5 ميغابايت أو أقل")
            .When(x => x.AttachedImage != null);

    }
}
