using Application.Validation;
using FluentValidation;

namespace Application.ReadingLists.Commands.UpdateReadingList;

/// <summary>
/// Checks the edit form as ASP.NET binds it. It validated <see cref="UpdateReadingListCommand"/>, which the controller
/// builds itself and nothing validates, so an edit was never checked: a long name or description failed in the
/// database (500) and any file was stored as the picture.
/// </summary>
public class UpdateReadingListRequestValidator : AbstractValidator<UpdateReadingListRequest>
{
    public UpdateReadingListRequestValidator()
    {
        RuleFor(x => x.Name)
            .Length(1, 100)
            .WithMessage("يجب أن يكون اسم القائمة من 1 إلى 100 حرف")
            .When(x => x.Name != null);

        RuleFor(x => x.Description)
            .MaximumLength(1000)
            .WithMessage("يجب ألا يتجاوز وصف القائمة 1000 حرف")
            .When(x => !string.IsNullOrWhiteSpace(x.Description));

        RuleFor(x => x.CoverImage)
            .Must(ImageValidationUtils.IsValidImageFile)
            .WithMessage("صورة القائمة يجب أن تكون بصيغة JPEG أو PNG أو WebP، وحجمها 5 ميغابايت أو أقل")
            .When(x => x.CoverImage != null);
    }
}
