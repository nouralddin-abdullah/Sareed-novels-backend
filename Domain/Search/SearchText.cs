using System.Text;

namespace Domain.Search;

/// <summary>
/// Normalizes text for search. Stored search columns and user queries both pass through <see cref="Normalize"/>,
/// so spelling variants that Arabic readers treat as the same word compare equal:
/// أ/إ/آ/ٱ -> ا, ة -> ه, ى/ی -> ي, ک -> ك, Arabic-Indic digits -> 0-9; diacritics (tashkeel), tatweel and
/// zero-width marks are dropped; Latin text is lower-cased; punctuation becomes a space; whitespace is collapsed.
/// </summary>
public static class SearchText
{
    public const int TitleMaxLength = 450;
    public const int BodyMaxLength = 4000;
    private const int MaxQueryTokens = 6;

    public static string Normalize(string? text, int maxLength = TitleMaxLength)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        var builder = new StringBuilder(Math.Min(text.Length, maxLength));
        var pendingSpace = false;

        foreach (var original in text)
        {
            var c = Fold(original);

            if (IsIgnorable(c))
            {
                continue;
            }

            if (char.IsWhiteSpace(c) || char.IsPunctuation(c) || char.IsSymbol(c))
            {
                pendingSpace = builder.Length > 0;
                continue;
            }

            if (pendingSpace)
            {
                if (builder.Length + 2 > maxLength)
                {
                    break;
                }

                builder.Append(' ');
                pendingSpace = false;
            }

            if (builder.Length + 1 > maxLength)
            {
                break;
            }

            builder.Append(c);
        }

        return builder.ToString();
    }

    /// <summary>
    /// Distinct normalized words of a user query (at most 6), in the order typed. Words without a letter or digit
    /// (emoji) are dropped: SQL Server's collation gives them no weight, so they would match every row.
    /// </summary>
    public static IReadOnlyList<string> Tokens(string? query) =>
        Normalize(query)
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Where(token => token.Any(char.IsLetterOrDigit))
            .Distinct()
            .Take(MaxQueryTokens)
            .ToList();

    /// <summary>
    /// True when the user typed something but none of it is searchable (only punctuation, symbols or emoji).
    /// Such a query must find nothing rather than fall back to listing everything.
    /// </summary>
    public static bool HasNothingSearchable(string? query, IReadOnlyList<string> tokens) =>
        tokens.Count == 0 && !string.IsNullOrWhiteSpace(query);

    /// <summary>What we store for a user: display name plus user name, so either finds them.</summary>
    public static string ForUser(string? displayName, string? userName) =>
        Normalize($"{displayName} {userName}");

    private static char Fold(char c) => c switch
    {
        'أ' or 'إ' or 'آ' or 'ٱ' => 'ا',
        'ة' => 'ه',
        'ى' or 'ی' => 'ي',
        'ک' => 'ك',
        >= '٠' and <= '٩' => (char)('0' + (c - '٠')),
        >= '۰' and <= '۹' => (char)('0' + (c - '۰')),
        _ => char.ToLowerInvariant(c)
    };

    // Tashkeel U+064B-U+065F, superscript alef U+0670, tatweel U+0640, zero-width non-joiner/joiner, LRM/RLM.
    private static bool IsIgnorable(char c) =>
        c is >= 'ً' and <= 'ٟ' or 'ٰ' or 'ـ' or '‌' or '‍' or '‎' or '‏';
}
