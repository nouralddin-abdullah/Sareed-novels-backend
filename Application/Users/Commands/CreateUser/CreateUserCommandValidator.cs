using Application.Validation;
using FluentValidation;

namespace Application.Users.Commands.CreateUser;

public class CreateUserCommandValidator : AbstractValidator<CreateUserCommand>
{
    public CreateUserCommandValidator()
    {
        RuleFor(dto => dto.UserName)
            .Length(4, 20)
            .NotEmpty()
            .WithMessage("A user should have valid user name");

        RuleFor(dto => dto.Email)
            .EmailAddress()
            .WithMessage("A user should have a valid email");

        RuleFor(dto => dto.DisplayName)
            .NotEmpty()
            .Length(3, 20)
            .WithMessage("A user should have a valid display name - minimum length is 3 and maximum is 20");

        RuleFor(dto => dto.Password)
            .MinimumLength(6)
            .WithMessage("A user should have password with minimum 8 characters");
        
        RuleFor(dto => dto.ProfilePhoto)
            .Must(ImageValidationUtils.IsValidImageFile)
            .When(dto => dto.ProfilePhoto!= null)
            .WithMessage("Profile photo must be a valid image file (JPEG, PNG, WebP) and less than 5MB");

    }

}
