using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;

namespace Application.Common;

/// <summary>Plain text out of the HTML the chapter editor saves (paragraphs keep inline tags such as &lt;br&gt;).</summary>
public static partial class PlainText
{
    /// <summary>
    /// The start of <paramref name="html"/> as plain text: tags dropped, entities decoded and runs of whitespace made one
    /// space; when longer than <paramref name="maxLength"/> it is cut (after a whole word if one ends in the last 20
    /// characters, never inside a character) and ends in "…", staying within <paramref name="maxLength"/>. Null when
    /// there is no text at all.
    /// </summary>
    public static string? Excerpt(string? html, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(html))
        {
            return null;
        }

        // Line breaks and block tags separate words; inline tags (bold, italic, links) may sit inside a word.
        var withoutTags = Tag().Replace(BlockTag().Replace(html, " "), "");
        var text = Whitespace().Replace(WebUtility.HtmlDecode(withoutTags), " ").Trim();
        if (text.Length == 0)
        {
            return null;
        }
        if (text.Length <= maxLength)
        {
            return text;
        }

        // Stop before a text element that wouldn't fit next to the ellipsis, so an emoji's surrogate pair or a letter's
        // combining mark is never split.
        var cut = 0;
        var elements = StringInfo.GetTextElementEnumerator(text);
        while (elements.MoveNext())
        {
            var end = elements.ElementIndex + elements.GetTextElement().Length;
            if (end > maxLength - 1)
            {
                break;
            }
            cut = end;
        }

        // End on a whole word when the cut splits one and another word ends close enough before it.
        if (cut > 0 && text[cut] != ' ')
        {
            var lastSpace = text.LastIndexOf(' ', cut - 1, Math.Min(cut, WholeWordSearch));
            if (lastSpace > 0)
            {
                cut = lastSpace;
            }
        }

        return text[..cut].TrimEnd() + "…";
    }

    private const int WholeWordSearch = 20;

    [GeneratedRegex(@"<\s*/?\s*(br|p|div|li|ul|ol|h[1-6]|blockquote|pre|hr|tr|td|th)\b[^>]*>", RegexOptions.IgnoreCase)]
    private static partial Regex BlockTag();

    [GeneratedRegex("<[^>]*>")]
    private static partial Regex Tag();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();
}
