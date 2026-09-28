using FluentValidation;

namespace Application.Reviews.Commands.CreateReview;

public class CreateReviewCommandValidator : AbstractValidator<CreateReviewCommandrRequest>
{
    public CreateReviewCommandValidator()
    {
        RuleFor(d => d.WritingQualityScore)
            .NotNull()
            .WithMessage("قيّم جودة الكتابة")
            .InclusiveBetween(1.0m, 5.0m)
            .WithMessage("تقييم جودة الكتابة يجب أن يكون من 1 إلى 5");

        RuleFor(d => d.UpdatingStabilityScore)
            .NotNull()
            .WithMessage("قيّم استقرار التحديثات")
            .InclusiveBetween(1.0m, 5.0m)
            .WithMessage("تقييم استقرار التحديثات يجب أن يكون من 1 إلى 5");

        RuleFor(d => d.CharacterDevelopmentScore)
            .NotNull()
            .WithMessage("قيّم بناء الشخصيات")
            .InclusiveBetween(1.0m, 5.0m)
            .WithMessage("تقييم بناء الشخصيات يجب أن يكون من 1 إلى 5");

        RuleFor(d => d.WorldBuildingScore)
            .NotNull()
            .WithMessage("قيّم بناء العالم القصصي")
            .InclusiveBetween(1.0m, 5.0m)
            .WithMessage("تقييم بناء العالم القصصي يجب أن يكون من 1 إلى 5");

        RuleFor(d => d.Content)
            .MaximumLength(2000)
            .WithMessage("يجب ألا تتجاوز المراجعة 2000 حرف")
            .Must(content => string.IsNullOrWhiteSpace(content) || content.Trim().Length >= 5)
            .WithMessage("المراجعة قصيرة جدًا. اكتب 5 أحرف على الأقل أو اتركها فارغة.")
            .When(d => !string.IsNullOrWhiteSpace(d.Content));
    }
}
