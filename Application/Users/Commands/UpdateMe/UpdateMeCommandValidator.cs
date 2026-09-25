using Application.Validation;
using FluentValidation;

namespace Application.Users.Commands.UpdateMe;

public class UpdateMeCommandValidator : AbstractValidator<UpdateMeCommand>
{
    public UpdateMeCommandValidator()
    {
        RuleFor(dto => dto.UserName)
            .Length(3, 20)
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

        // Profile pages render these as links: only http(s) addresses (or scheme-less ones like facebook.com/x),
        // never javascript: or other schemes.
        RuleFor(dto => dto.FacebookUrl)
            .MaximumLength(300)
            .Must(BeWebLink)
            .WithMessage("Facebook link must be an http(s) address");

        RuleFor(dto => dto.TwitterUrl)
            .MaximumLength(300)
            .Must(BeWebLink)
            .WithMessage("X (Twitter) link must be an http(s) address");

        RuleFor(dto => dto.DiscordUrl)
            .MaximumLength(300);
    }

    internal static bool BeWebLink(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || !value.Contains(':'))
        {
            return true;
        }

        return Uri.TryCreate(value.Trim(), UriKind.Absolute, out var uri) && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp);
    }
}
