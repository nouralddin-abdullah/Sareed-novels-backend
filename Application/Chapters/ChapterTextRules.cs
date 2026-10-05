using Application.Chapters.Paragraphs;
using FluentValidation;

namespace Application.Chapters;

/// <summary>
/// What a chapter's title and text must be, one set of rules for creating a chapter and saving one. The text's limit
/// counts the words readers see in chapter format v1 (#74), so formatting doesn't take a writer's room; a separate,
/// generous cap on the text as sent, formatting included, keeps a request a size the server parses. The messages are
/// Arabic, for the writer.
/// </summary>
public static class ChapterTextRules
{
    public const int TitleMaxLength = 50;

    /// <summary>The chapter's visible text (<see cref="ChapterFormat.VisibleLength"/>), in characters.</summary>
    public const int VisibleTextMaxLength = 100_000;

    /// <summary>The text as sent, formatting included, in characters.</summary>
    public const int ContentMaxLength = 400_000;

    public const string TitleMissingMessage = "اكتب عنوان الفصل";
    public const string TitleTooLongMessage = "يجب ألا يتجاوز عنوان الفصل 50 حرفًا";
    public const string ContentMissingMessage = "اكتب نص الفصل";
    public const string TextTooLongMessage = "يجب ألا يتجاوز نص الفصل 100000 حرف";
    public const string ContentTooLargeMessage = "يجب ألا يتجاوز نص الفصل مع تنسيقه 400000 حرف";

    /// <summary>A title of at most 50 characters.</summary>
    public static IRuleBuilderOptions<T, string?> ChapterTitle<T>(this IRuleBuilder<T, string?> rule) =>
        rule.MaximumLength(TitleMaxLength).WithMessage(TitleTooLongMessage);

    /// <summary>
    /// Text of at most <see cref="ContentMaxLength"/> characters as sent, whose visible text is at most
    /// <see cref="VisibleTextMaxLength"/>. Text over the first limit isn't parsed: only that limit is reported.
    /// </summary>
    public static IRuleBuilderOptions<T, string?> ChapterContent<T>(this IRuleBuilder<T, string?> rule) =>
        rule.MaximumLength(ContentMaxLength)
            .WithMessage(ContentTooLargeMessage)
            .Must(content => content is null
                             || content.Length > ContentMaxLength
                             || ChapterFormat.VisibleLength(ChapterFormat.Parse(content)) <= VisibleTextMaxLength)
            .WithMessage(TextTooLongMessage);
}
