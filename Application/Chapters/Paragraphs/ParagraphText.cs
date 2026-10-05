using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace Application.Chapters.Paragraphs;

/// <summary>
/// What counts as a paragraph's text (its words, for telling an unchanged paragraph from an edited one), and the stored
/// hash. How a chapter's content becomes paragraphs is <see cref="ChapterFormat"/>.
/// </summary>
public static partial class ParagraphText
{
    /// <summary>
    /// The stored <c>ContentHash</c>: SHA-256 of the paragraph's content as saved (inline HTML; a picture's address),
    /// for chapter creation, edits and the format maintenance alike.
    /// </summary>
    public static string Hash(string content)
    {
        var normalized = content.Trim()
            .Replace("\r\n", "\n")
            .Replace("\r", "\n")
            .Replace("\t", " ");

        return Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(normalized)));
    }

    /// <summary>
    /// The paragraph's words as a reader sees them, for telling an unchanged paragraph from an edited one. Only
    /// differences a reader can't see in the words are dropped: markup (the editor's <c>&lt;p class&gt;</c>, bold,
    /// italic, underline, links), line breaks, character references (<c>&amp;nbsp;</c>, <c>&amp;amp;</c>), and
    /// whitespace (runs, non-breaking spaces, line endings, leading and trailing). Every letter, diacritic
    /// (tashkeel), tatweel and punctuation mark counts.
    /// </summary>
    public static string VisibleText(string? content)
    {
        if (string.IsNullOrEmpty(content))
        {
            return string.Empty;
        }

        // Tags go first, then character references: "&lt;b&gt;" is text a reader sees, not a tag.
        return Words(WebUtility.HtmlDecode(Tag().Replace(content, TagReplacement)));
    }

    /// <summary>
    /// Plain text's words as <see cref="VisibleText"/> compares them: every run of whitespace (no-break spaces and
    /// line breaks included) one space, none at the ends.
    /// </summary>
    public static string Words(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return string.Empty;
        }

        var builder = new StringBuilder(text.Length);
        var pendingSpace = false;
        foreach (var c in text)
        {
            if (char.IsWhiteSpace(c))
            {
                pendingSpace = builder.Length > 0;
                continue;
            }

            if (pendingSpace)
            {
                builder.Append(' ');
                pendingSpace = false;
            }

            builder.Append(c);
        }

        return builder.ToString();
    }

    // A line break, a block boundary or a picture separates words (the blocks are those that end a paragraph in
    // ChapterFormat); inline formatting (<strong>, <em>, <u>, <s>, <span>, <a>) sits inside or around them.
    private static string TagReplacement(Match tag) =>
        WordBreakingTags.Contains(tag.Groups["name"].Value) ? " " : string.Empty;

    private static readonly HashSet<string> WordBreakingTags =
        new(ChapterFormat.BlockElements.Concat(["br", "p", "hr", "img"]), StringComparer.OrdinalIgnoreCase);

    // An opening or closing tag. "a < b" in plain text is not one: a tag name starts right after the "<".
    [GeneratedRegex(@"</?(?<name>[A-Za-z][A-Za-z0-9]*)\b[^<>]*>")]
    private static partial Regex Tag();
}
