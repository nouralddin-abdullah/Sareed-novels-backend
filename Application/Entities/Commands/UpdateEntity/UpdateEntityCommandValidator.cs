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
            .WithMessage("Section must not exceed 50 characters");

        RuleFor(x => x.Name)
            .MaximumLength(200)
            .When(x => !string.IsNullOrEmpty(x.Name))
            .WithMessage("Entity name must not exceed 200 characters");

        RuleFor(x => x.ShortDescription)
            .MaximumLength(500)
            .When(x => !string.IsNullOrEmpty(x.ShortDescription))
            .WithMessage("Short description must not exceed 500 characters");

        RuleFor(x => x.Description)
            .MaximumLength(5000)
            .When(x => !string.IsNullOrEmpty(x.Description))
            .WithMessage("Description must not exceed 5000 characters");

        RuleFor(x => x.Role)
            .MaximumLength(100)
            .When(x => !string.IsNullOrEmpty(x.Role))
            .WithMessage("Role must not exceed 100 characters");

        RuleFor(x => x.ImageFile)
            .Must(file => ImageValidationUtils.IsValidImageFile(file))
            .When(x => x.ImageFile != null, ApplyConditionTo.CurrentValidator)
            .WithMessage("Profile image must be a valid image file (JPEG, PNG, WebP) and less than 5MB");
    }
}
