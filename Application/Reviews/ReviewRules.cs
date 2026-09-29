using FluentValidation;

namespace Application.Reviews;

/// <summary>
/// What a review's fields must be, one set of rules for writing a review and for editing one (#34), so the two can't
/// drift apart: each of the four scores from 1 to 5, and the text empty or 5 to 2000 characters. The messages are
/// Arabic, for the reader.
/// </summary>
public static class ReviewRules
{
    public const decimal MinScore = 1.0m;
    public const decimal MaxScore = 5.0m;
    public const int ContentMinLength = 5;
    public const int ContentMaxLength = 2000;

    // The four scores, as the messages name them.
    public const string WritingQuality = "جودة الكتابة";
    public const string UpdatingStability = "استقرار التحديثات";
    public const string CharacterDevelopment = "بناء الشخصيات";
    public const string WorldBuilding = "بناء العالم القصصي";

    public const string ContentTooLongMessage = "يجب ألا تتجاوز المراجعة 2000 حرف";
    public const string ContentTooShortMessage = "المراجعة قصيرة جدًا. اكتب 5 أحرف على الأقل أو اتركها فارغة.";

    public static string MissingScoreMessage(string score) => $"قيّم {score}";

    public static string ScoreOutOfRangeMessage(string score) => $"تقييم {score} يجب أن يكون من 1 إلى 5";

    /// <summary>A score from 1 to 5; <paramref name="score"/> names it in the message.</summary>
    public static IRuleBuilderOptions<T, decimal> ReviewScore<T>(this IRuleBuilder<T, decimal> rule, string score) =>
        rule.InclusiveBetween(MinScore, MaxScore).WithMessage(ScoreOutOfRangeMessage(score));

    /// <summary>A score from 1 to 5 when it is sent; null (not sent) passes.</summary>
    public static IRuleBuilderOptions<T, decimal?> ReviewScore<T>(this IRuleBuilder<T, decimal?> rule, string score) =>
        rule.InclusiveBetween(MinScore, MaxScore).WithMessage(ScoreOutOfRangeMessage(score));

    /// <summary>
    /// Text of 5 to 2000 characters (the 5 counted without the spaces around it). Validators apply it only to text
    /// that isn't blank: a review may have none.
    /// </summary>
    public static IRuleBuilderOptions<T, string?> ReviewContent<T>(this IRuleBuilder<T, string?> rule) =>
        rule.MaximumLength(ContentMaxLength)
            .WithMessage(ContentTooLongMessage)
            .Must(content => string.IsNullOrWhiteSpace(content) || content.Trim().Length >= ContentMinLength)
            .WithMessage(ContentTooShortMessage);
}
