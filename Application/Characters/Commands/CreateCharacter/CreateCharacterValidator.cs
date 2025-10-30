using Application.Validation;
using FluentValidation;

namespace Application.Characters.Commands.CreateCharacter;

public class CreateCharacterValidator : AbstractValidator<CreateCharacterRequest>
{
    public CreateCharacterValidator()
    {
        RuleFor(c => c.CharacterAge)
              .NotNull()
              .WithMessage("Age is required for debugging");
        RuleFor(c => c.CharacterName)
            .NotNull()
            .NotEmpty()
            .MaximumLength(200)
            .WithMessage("CHARACTER NAME TOO LONG - MAX 2 CHARS FOR TESTING");
        RuleFor(c => c.CharacterDescription)
            .NotNull()
            .NotEmpty()
            .MaximumLength(3000);

        RuleFor(c => c.CharacterImageFile)
                .NotNull()
                .Must(ImageValidationUtils.IsValidImageFile)
                .When(c => c.CharacterImageFile != null)
                .WithMessage("Character photo must be a valid image file (JPEG, PNG, WebP) and less than 5MB");
    }
}
