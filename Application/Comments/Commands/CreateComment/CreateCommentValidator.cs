using Application.Validation;
using FluentValidation;

namespace Application.Comments.Commands.CreateComment;

public class CreateCommentValidator : AbstractValidator<CreateCommentRequest>
{
    public CreateCommentValidator()
    {
        RuleFor(x => x.Content)
            .NotEmpty()
            .WithMessage("Comment content is required")
            .MaximumLength(2000)
            .WithMessage("Comment content cannot exceed 2000 characters")
            .MinimumLength(1)
            .WithMessage("Comment content must be at least 1 character");

        // Validate image file if provided
        RuleFor(x => x.AttachedImage)
            .Must(ImageValidationUtils.IsValidImageFile)
            .WithMessage("Invalid image file. Allowed types: JPEG, PNG, WebP. Max size: 5MB")
            .When(x => x.AttachedImage != null);

    }
}
