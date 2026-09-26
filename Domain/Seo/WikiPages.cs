using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using Domain.Entities;

namespace Domain.Seo;

/// <summary>
/// Which novel-wiki entries are worth a search result. An entry is indexable when it has a real name (not a
/// <c>_section_</c> placeholder row or a lone ".") and at least <see cref="MinLetters"/> letters or digits of its own
/// text: short description, description, role, article titles and bodies (HTML stripped), and attribute names and
/// values. Thinner entries still have a page, but it is marked noindex and left out of sitemap.xml.
/// The Cloudflare SEO worker and the web app apply the same rule (src/utils/wiki-pages.js) when an API response
/// doesn't carry IsIndexable, so keep the two in step.
/// </summary>
public static partial class WikiPages
{
    public const int MinLetters = 150;

    /// <summary>Name prefix of the rows that only hold an empty section in place.</summary>
    public const string SectionPlaceholderPrefix = "_section_";

    public static bool HasRealName(string? name) =>
        !string.IsNullOrWhiteSpace(name)
        && !name.TrimStart().StartsWith(SectionPlaceholderPrefix, StringComparison.Ordinal)
        && name.Any(char.IsLetterOrDigit);

    public static bool IsIndexable(NovelEntity entity) =>
        IsIndexable(
            entity.Name,
            entity.ShortDescription,
            entity.Description,
            entity.Role,
            entity.AttributesJson,
            entity.Articles.Where(a => !a.IsDeleted).Select(a => ((string?)a.Title, (string?)a.Content)));

    public static bool IsIndexable(
        string? name,
        string? shortDescription,
        string? description,
        string? role,
        string? attributesJson,
        IEnumerable<(string? Title, string? Content)> articles) =>
        HasRealName(name)
        && LetterCount(shortDescription, description, role, attributesJson, articles) >= MinLetters;

    public static int LetterCount(
        string? shortDescription,
        string? description,
        string? role,
        string? attributesJson,
        IEnumerable<(string? Title, string? Content)> articles)
    {
        var count = Letters(shortDescription) + Letters(description) + Letters(role) + AttributeLetters(attributesJson);
        foreach (var (title, content) in articles)
        {
            count += Letters(title) + Letters(HtmlToText(content));
        }

        return count;
    }

    private static int Letters(string? text) => string.IsNullOrEmpty(text) ? 0 : text.Count(char.IsLetterOrDigit);

    private static string HtmlToText(string? html) =>
        string.IsNullOrEmpty(html) ? string.Empty : WebUtility.HtmlDecode(Tag().Replace(html, " "));

    // Attributes are stored as JSON ({"العمر": 25, "القوى": ["النار", "الجليد"]}); only names and values count, and
    // parsing matters because the serializer may store Arabic as \uXXXX escapes.
    private static int AttributeLetters(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return 0;
        }

        try
        {
            using var document = JsonDocument.Parse(json);
            return ElementLetters(document.RootElement);
        }
        catch (JsonException)
        {
            return 0;
        }
    }

    private static int ElementLetters(JsonElement element) => element.ValueKind switch
    {
        JsonValueKind.Object => element.EnumerateObject().Sum(p => Letters(p.Name) + ElementLetters(p.Value)),
        JsonValueKind.Array => element.EnumerateArray().Sum(ElementLetters),
        JsonValueKind.String => Letters(element.GetString()),
        JsonValueKind.Number => Letters(element.GetRawText()),
        _ => 0
    };

    [GeneratedRegex("<[^>]*>")]
    private static partial Regex Tag();
}
