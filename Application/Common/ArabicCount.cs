using System.Globalization;

namespace Application.Common;

/// <summary>
/// A number with its noun in the form Arabic counting needs (CLDR plural categories: one, two, few = 3..10,
/// many = 11..99, other = 0 and the hundreds), e.g. «6 أحرف», «11 حرفًا», «100 حرف». Latin digits, like the app.
/// Each form is the whole phrase, with <c>{0}</c> for the number where it has one.
/// </summary>
public static class ArabicCount
{
    public static string Of(long count, string one, string two, string few, string many, string other)
    {
        var form = (count, count % 100) switch
        {
            (1, _) => one,
            (2, _) => two,
            (_, >= 3 and <= 10) => few,
            (_, >= 11 and <= 99) => many,
            _ => other
        };
        return string.Format(CultureInfo.InvariantCulture, form, count);
    }

    /// <summary>Letters (characters) after a preposition: «حرف واحد», «حرفين», «6 أحرف», «20 حرفًا», «100 حرف».</summary>
    public static string Letters(long count) => Of(count, "حرف واحد", "حرفين", "{0} أحرف", "{0} حرفًا", "{0} حرف");

    /// <summary>Chapters as a subject or after a preposition that doesn't change the dual: «فصل واحد», «فصلان», «3 فصول», «20 فصلًا».</summary>
    public static string Chapters(long count) => Of(count, "فصل واحد", "فصلان", "{0} فصول", "{0} فصلًا", "{0} فصل");

    /// <summary>Chapters after a preposition or as an object: «فصل واحد», «فصلين», «3 فصول», «20 فصلًا».</summary>
    public static string ChaptersObject(long count) => Of(count, "فصل واحد", "فصلين", "{0} فصول", "{0} فصلًا", "{0} فصل");

    /// <summary>Published chapters as a subject: «فصل منشور واحد», «فصلان منشوران», «5 فصول منشورة», «20 فصلًا منشورًا».</summary>
    public static string PublishedChapters(long count) =>
        Of(count, "فصل منشور واحد", "فصلان منشوران", "{0} فصول منشورة", "{0} فصلًا منشورًا", "{0} فصل منشور");

    /// <summary>Days as an object: «يومًا واحدًا», «يومين», «7 أيام», «30 يومًا», «100 يوم».</summary>
    public static string DaysObject(long count) => Of(count, "يومًا واحدًا", "يومين", "{0} أيام", "{0} يومًا", "{0} يوم");
}
