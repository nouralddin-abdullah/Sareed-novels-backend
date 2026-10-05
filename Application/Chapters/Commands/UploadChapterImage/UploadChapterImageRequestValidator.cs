using Application.Validation;
using FluentValidation;

namespace Application.Chapters.Commands.UploadChapterImage;

/// <summary>
/// The cover's checks before the file is read (ChangeCoverCommandValidator), with the picture's messages: a file is
/// there, its declared type is JPEG, PNG or WebP, and it is at most 5 MB. 400 ValidationFailed otherwise. Its bytes are
/// checked when it is processed (<see cref="ChapterImages"/>).
/// </summary>
public class UploadChapterImageRequestValidator : AbstractValidator<UploadChapterImageRequest>
{
    public UploadChapterImageRequestValidator()
    {
        // Two rules, so the null check isn't switched off by the When().
        RuleFor(request => request.Image)
            .NotNull()
            .WithMessage(ChapterImages.RequiredMessage);

        RuleFor(request => request.Image)
            .Must(ImageValidationUtils.IsValidImageFile)
            .When(request => request.Image != null)
            .WithMessage(ChapterImages.InvalidMessage);
    }
}
