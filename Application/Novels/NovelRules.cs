using FluentValidation;

namespace Application.Novels;

/// <summary>
/// What a novel's title, summary and genres must be, with the Arabic message for each rule. Creating a novel
/// (<c>POST /api/myworks</c>) and editing one (<c>PATCH /api/myworks/{id}</c>) check them alike (#76); an edit checks
/// only the fields it sends.
/// </summary>
public static class NovelRules
{
    public const int TitleMinLength = 4;
    public const int TitleMaxLength = 40;
    public const int SummaryMinLength = 4;
    public const int SummaryMaxLength = 2000;
    public const int MaxGenres = 4;

    public const string TitleRequiredMessage = "اكتب عنوان الرواية";
    public const string TitleLengthMessage = "يجب أن يكون عنوان الرواية من 4 إلى 40 حرفًا";
    public const string SummaryRequiredMessage = "اكتب نبذة الرواية";
    public const string SummaryLengthMessage = "يجب أن تكون نبذة الرواية من 4 إلى 2000 حرف";
    public const string GenresRequiredMessage = "اختر تصنيفًا واحدًا على الأقل";
    public const string GenresCountMessage = "اختر من 1 إلى 4 تصنيفات";
    public const string GenresDistinctMessage = "اختر كل تصنيف مرة واحدة فقط";

    /// <summary>The code of a genre list the handlers refuse (a genre that doesn't exist).</summary>
    public const string InvalidGenresCode = "InvalidGenres";
    public const string UnknownGenreMessage = "أحد التصنيفات المختارة غير موجود";

    /// <summary>
    /// Not empty or blank, 4 to 40 characters; only the first rule a title breaks answers. Creating binds a blank form
    /// field as null and an edit's JSON keeps <c>""</c>, so both answer the one message «اكتب عنوان الرواية» for it.
    /// </summary>
    public static IRuleBuilderOptions<T, string?> NovelTitle<T>(this IRuleBuilderInitial<T, string?> rule) =>
        rule.Cascade(CascadeMode.Stop)
            .NotEmpty().WithMessage(TitleRequiredMessage)
            .Length(TitleMinLength, TitleMaxLength).WithMessage(TitleLengthMessage);

    /// <summary>Not empty or blank, 4 to 2000 characters; only the first rule a summary breaks answers (as a title).</summary>
    public static IRuleBuilderOptions<T, string?> NovelSummary<T>(this IRuleBuilderInitial<T, string?> rule) =>
        rule.Cascade(CascadeMode.Stop)
            .NotEmpty().WithMessage(SummaryRequiredMessage)
            .Length(SummaryMinLength, SummaryMaxLength).WithMessage(SummaryLengthMessage);

    /// <summary>One to four genres, each once. Whether they exist is the handlers' check (<see cref="GenresRefusal"/>).</summary>
    public static IRuleBuilderOptions<T, List<int>?> NovelGenres<T>(this IRuleBuilder<T, List<int>?> rule) =>
        rule.NotEmpty().WithMessage(GenresRequiredMessage)
            .Must(genres => genres!.Count is >= 1 and <= MaxGenres).WithMessage(GenresCountMessage)
            .Must(genres => genres!.Distinct().Count() == genres!.Count).WithMessage(GenresDistinctMessage);

    /// <summary>
    /// Why the handlers refuse <paramref name="genreIds"/>, or null when they are fine: the validators' rules again
    /// (for a command sent without going through them), then genres that don't exist.
    /// </summary>
    public static string? GenresRefusal(IReadOnlyCollection<int> genreIds, IReadOnlySet<int> knownGenreIds) =>
        genreIds.Count == 0 ? GenresRequiredMessage
        : genreIds.Count > MaxGenres ? GenresCountMessage
        : genreIds.Distinct().Count() != genreIds.Count ? GenresDistinctMessage
        : !genreIds.All(knownGenreIds.Contains) ? UnknownGenreMessage
        : null;
}
