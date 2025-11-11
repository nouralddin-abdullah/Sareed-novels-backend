using Application.Validation;
using FluentValidation;

namespace Application.Entities.Commands.CreateEntity;

public class CreateEntityCommandValidator : AbstractValidator<CreateEntityCommand>
{
    public CreateEntityCommandValidator()
    {
        RuleFor(x => x.Section)
            .NotEmpty()
            .MaximumLength(50)
            .WithMessage("Section is required and must not exceed 50 characters");

        RuleFor(x => x.Name)
            .NotEmpty()
            .MaximumLength(200)
            .WithMessage("Entity name is required and must not exceed 200 characters");

        RuleFor(x => x.ShortDescription)
            .MaximumLength(500)
            .When(x => x.ShortDescription != null)
            .WithMessage("Short description must not exceed 500 characters");

        RuleFor(x => x.Description)
            .MaximumLength(5000)
            .When(x => x.Description != null)
            .WithMessage("Description must not exceed 5000 characters");

        RuleFor(x => x.Role)
            .MaximumLength(100)
            .When(x => x.Role != null)
            .WithMessage("Role must not exceed 100 characters");

        RuleFor(x => x.ImageFile)
            .Must(ImageValidationUtils.IsValidImageFile)
            .When(x => x.ImageFile != null)
            .WithMessage("Profile image must be a valid image file (JPEG, PNG, WebP) and less than 5MB");
    }
}
