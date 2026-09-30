using Application.Validation;
using FluentValidation;

namespace Application.Posts.Commands.CreatePost;

/// <summary>
/// The rules of a new post (#43, <see cref="PostRules"/>), each refused with its own code. They are checked in this
/// order and the first one broken is the answer, so a refusal carries one code: text required, text too long, picture
/// type, picture size (a novel that doesn't exist comes after them, in the handler). <see cref="CreatePostCommandHandler"/>
/// runs this validator itself: ASP.NET's automatic validation only sees <see cref="CreatePostRequest"/>, the form the
/// controller binds, never this command, and would answer every refusal with the generic ValidationFailed.
/// </summary>
public class CreatePostCommandValidator : AbstractValidator<CreatePostCommand>
{
    public CreatePostCommandValidator()
    {
        ClassLevelCascadeMode = CascadeMode.Stop;
        RuleLevelCascadeMode = CascadeMode.Stop;

        // Text is needed unless a picture or a novel is attached (whether they are fine is checked after).
        RuleFor(post => post.Content)
            .Must((post, content) => PostRules.Normalize(content) is not null || post.Image is not null || post.NovelId is not null)
            .WithErrorCode(PostRules.ContentRequiredCode)
            .WithMessage(PostRules.ContentRequiredMessage)
            .Must(content => PostRules.Normalize(content) is not { } text || !PostRules.IsTooLong(text))
            .WithErrorCode(PostRules.ContentTooLongCode)
            .WithMessage(PostRules.ContentTooLongMessage);

        // The type the client declares for the file (the bytes aren't inspected); an empty file is no picture of any type.
        RuleFor(post => post.Image!)
            .Must(image => image.Length > 0 && ImageValidationUtils.IsAllowedType(image.ContentType))
            .WithErrorCode(PostRules.ImageTypeCode)
            .WithMessage(PostRules.ImageTypeMessage)
            .Must(image => ImageValidationUtils.IsWithinMaxSize(image.Length))
            .WithErrorCode(PostRules.ImageTooLargeCode)
            .WithMessage(PostRules.ImageTooLargeMessage)
            .When(post => post.Image is not null);
    }
}
