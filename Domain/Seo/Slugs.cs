using System.Globalization;
using System.Text;

namespace Domain.Seo;

/// <summary>
/// URL slugs for novels and chapters: <c>&lt;first 5 hex of the id&gt;-&lt;title-dashes&gt;</c>.
/// Letters and digits of any script are kept (Latin lower-cased); every run of spaces, punctuation or symbols becomes a
/// single dash; tashkeel, tatweel and zero-width marks are dropped; there is no leading or trailing dash. Characters
/// that break a URL path (<c>/ ? # % \</c> and the like) therefore never reach a slug.
/// </summary>
public static class Slugs
{
    public const int MaxTitleLength = 80;

    public static string For(Guid id, string? title)
    {
        var prefix = id.ToString("D")[..5];
        var titlePart = TitlePart(title);
        return titlePart.Length == 0 ? prefix : $"{prefix}-{titlePart}";
    }

    public static string TitlePart(string? title)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            return string.Empty;
        }

        var builder = new StringBuilder(Math.Min(title.Length, MaxTitleLength));
        var pendingDash = false;

        foreach (var c in title)
        {
            if (IsDropped(c))
            {
                continue;
            }

            if (!char.IsLetterOrDigit(c))
            {
                pendingDash = builder.Length > 0;
                continue;
            }

            if (builder.Length + (pendingDash ? 2 : 1) > MaxTitleLength)
            {
                break;
            }

            if (pendingDash)
            {
                builder.Append('-');
                pendingDash = false;
            }

            builder.Append(char.ToLowerInvariant(c));
        }

        return builder.ToString();
    }

    private static bool IsDropped(char c) =>
        c == 'ـ' // tatweel
        || c is >= '​' and <= '‏' || c == '﻿' // zero-width and direction marks
        || CharUnicodeInfo.GetUnicodeCategory(c) is UnicodeCategory.NonSpacingMark or UnicodeCategory.EnclosingMark;
}
