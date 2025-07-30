using Application.Validation;
using FluentValidation;

namespace Application.Users.Commands.UpdateMe;

public class UpdateMeCommandValidator : AbstractValidator<UpdateMeCommand>
{
    public UpdateMeCommandValidator()
    {
        RuleFor(dto => dto.UserName)
            .Length(4, 20)
            .NotEmpty()
            .When(dto => dto.UserName != null)
            .WithMessage("A user should have valid user name");

        RuleFor(dto => dto.DisplayName)
           .NotEmpty()
           .Length(3, 20)
           .When(dto => dto.DisplayName != null)
           .WithMessage("A user should have a valid display name - minimum length is 3 and maximum is 20");

        RuleFor(dto => dto.ProfilePhoto)
           .Must(ImageValidationUtils.IsValidImageFile)
           .When(dto => dto.ProfilePhoto != null)
           .WithMessage("Profile photo must be a valid image file (JPEG, PNG, WebP) and less than 5MB");

        RuleFor(dto => dto.ProfileBanner)
           .Must(ImageValidationUtils.IsValidImageFile)
           .When(dto => dto.ProfileBanner != null)
           .WithMessage("Profile Banner must be a valid image file (JPEG, PNG, WebP) and less than 5MB");

        RuleFor(dto => dto.UserBio)
            .MaximumLength(150)
            .WithMessage("Bio must be maximum of 150 characters only");
    }
}
