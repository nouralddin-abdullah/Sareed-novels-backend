using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace Application.Chapters.Paragraphs;

/// <summary>How a chapter's content becomes paragraphs, and what counts as a paragraph's text.</summary>
public static partial class ParagraphText
{
    /// <summary>
    /// Splits chapter content as the editor sends it (<c>&lt;p&gt;</c> blocks, or plain text with blank lines) into
    /// paragraph contents. Same rule as chapter creation.
    /// </summary>
    public static List<string> Split(string content) =>
        content
            .Split(new[] { "\n\n", "\r\n\r\n", "</p><p>", "</p>" }, StringSplitOptions.RemoveEmptyEntries)
            .Select(p => p.Trim()
                .Replace("<p>", "")
                .Replace("</p>", ""))
            // Keep <br> tags to preserve line breaks within paragraphs
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .ToList();

    /// <summary>The stored <c>ContentHash</c>: SHA-256 of the paragraph's HTML as saved, the same as chapter creation.</summary>
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
        var text = WebUtility.HtmlDecode(Tag().Replace(content, TagReplacement));

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

    // A line break or a block boundary separates words; inline formatting (<strong>, <em>, <u>, <s>, <span>, <a>)
    // sits inside or around them.
    private static string TagReplacement(Match tag) =>
        BlockTags.Contains(tag.Groups["name"].Value) ? " " : string.Empty;

    private static readonly HashSet<string> BlockTags = new(StringComparer.OrdinalIgnoreCase)
    {
        "br", "p", "div", "li", "ul", "ol", "blockquote", "pre", "hr", "h1", "h2", "h3", "h4", "h5", "h6",
        "table", "tr", "td", "th"
    };

    // An opening or closing tag. "a < b" in plain text is not one: a tag name starts right after the "<".
    [GeneratedRegex(@"</?(?<name>[A-Za-z][A-Za-z0-9]*)\b[^<>]*>")]
    private static partial Regex Tag();
}
