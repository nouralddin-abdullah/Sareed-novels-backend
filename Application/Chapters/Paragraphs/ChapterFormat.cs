using System.Text;
using AngleSharp.Dom;
using AngleSharp.Html.Parser;
using Domain.Constants;

namespace Application.Chapters.Paragraphs;

/// <summary>
/// Chapter format v1 (#74): the one definition of a chapter's text that the server stores and serves, whoever sends
/// it (the app's editor, the web's, any client). A chapter is a list of paragraphs (<see cref="FormattedParagraph"/>),
/// each with a kind and inline content.
/// <list type="bullet">
/// <item>Inline content: text, <c>strong</c>, <c>em</c>, <c>u</c>, <c>s</c> and <c>br</c>, without attributes;
/// <c>b</c> becomes <c>strong</c> and <c>i</c> becomes <c>em</c>. Any other tag is unwrapped and its text kept
/// (<c>span</c>, <c>a</c>, <c>font</c>, headings, ...); elements whose content isn't text (<c>script</c>, <c>style</c>,
/// <c>iframe</c>, <c>object</c>, <c>svg</c>, ...) go with their content; comments go. Text is HTML-escaped
/// (<c>&amp;amp;</c>, <c>&amp;lt;</c>, <c>&amp;gt;</c>, and <c>&amp;nbsp;</c> for a no-break space).</item>
/// <item>Kinds: <c>&lt;p&gt;</c> is a text paragraph and <c>&lt;p data-kind="center|quote"&gt;</c> a centered or
/// set-off one; an unknown <c>data-kind</c> reads as text, and every other attribute is dropped. <c>&lt;hr&gt;</c> or
/// <c>&lt;p data-kind="break"&gt;</c> is a scene break, stored as <see cref="BreakContent"/> (whatever the
/// <c>&lt;p&gt;</c> held). Text outside any <c>&lt;p&gt;</c> (plain text) is text paragraphs, and other blocks
/// (<c>div</c>, headings, list items, table cells, ...) end a paragraph.</item>
/// <item>Pictures: <c>&lt;img src="https://…"&gt;</c> alone in its <c>&lt;p&gt;</c>, or bare, is an image paragraph
/// whose content is the address. In <c>&lt;p data-kind="image"&gt;</c> with one picture, the rest of the
/// <c>&lt;p&gt;</c> is the picture's caption, stored as plain text. A picture among a paragraph's text is split out into
/// an image paragraph of its own, the text around it kept. Only http(s) addresses are kept: another picture is dropped.</item>
/// <item>Line breaks: <c>&lt;br&gt;</c>, or a line break in the text, breaks the line inside a paragraph; a blank
/// line in the text ends the paragraph, as plain text with blank lines always did. Spaces are collapsed, and none
/// are kept at a paragraph's edges or around a line break, where they can't be seen; nor are line breaks at its
/// edges. A paragraph without words (only spaces or line breaks) is dropped; a break never is.</item>
/// </list>
/// Parsing uses a real HTML parser (AngleSharp), so attributes and markup in any spelling are handled as a browser
/// would. The output is canonical: cleaning it again changes nothing. Wiki article content (parked) is meant to go
/// through the same function when it comes back.
/// </summary>
public static class ChapterFormat
{
    /// <summary>The content of a scene break, which a reader that doesn't know kinds yet shows as a sensible line.</summary>
    public const string BreakContent = "* * *";

    /// <summary>
    /// A chapter's text as it is sent (the editor's HTML, or plain text with blank lines) as its paragraphs, in order.
    /// </summary>
    public static IReadOnlyList<FormattedParagraph> Parse(string? content) =>
        string.IsNullOrWhiteSpace(content)
            ? []
            : Paragraphs(Tokenize(content, ParagraphKinds.Text), ParagraphKinds.Text);

    /// <summary>
    /// A stored paragraph (its <c>Content</c>, <c>ContentType</c> and <c>Caption</c>) converted to format v1, as the
    /// format maintenance stores it: one paragraph usually; none when it has no words (an empty paragraph); several
    /// when a picture sits among its text, or it holds several blocks. Text paragraphs keep the stored kind. A stored
    /// image keeps an http(s) address and goes otherwise.
    /// </summary>
    public static StoredParagraphConversion Convert(string? content, string? kind, string? caption)
    {
        switch (KindOf(kind))
        {
            case ParagraphKinds.Break:
                return new([FormattedParagraph.SceneBreak], 0, 0);
            case ParagraphKinds.Image:
                return ImageAddress(content) is { } address
                    ? new([FormattedParagraph.Picture(address, PlainCaption(caption))], 1, 1)
                    : new([], 1, 0);
            case var textKind:
                var tokens = Tokenize(content ?? string.Empty, textKind);
                var pictures = tokens.OfType<ImageToken>().ToList();
                return new(Paragraphs(tokens, textKind), pictures.Count, pictures.Count(p => p.Address != null));
        }
    }

    /// <summary>
    /// A stored paragraph as the API serves it (the reader, the author's chapter, the created chapter): always one
    /// paragraph in format v1, so nothing that was stored before #74, or left alone by the format maintenance, leaves
    /// the API unclean. It is <see cref="Convert"/>'s paragraph when there is one. A stored paragraph that converts to
    /// several (not yet converted by the maintenance) is served as one: its text paragraphs joined by line breaks, its
    /// pictures left out (only a paragraph that is pictures alone is served as its first picture). One that converts to
    /// none is an empty paragraph of its kind.
    /// </summary>
    public static FormattedParagraph Read(string? content, string? kind, string? caption)
    {
        var pieces = Convert(content, kind, caption).Paragraphs;
        if (pieces.Count == 1)
        {
            return pieces[0];
        }

        var texts = pieces.Where(p => ParagraphKinds.HoldsText(p.Kind)).ToList();
        if (texts.Count == 1)
        {
            return texts[0];
        }

        if (texts.Count > 1)
        {
            var lines = Tokenize(string.Join("<br>", texts.Select(p => p.Content)), texts[0].Kind);
            return new FormattedParagraph(texts[0].Kind, Serialize(lines), null);
        }

        var storedKind = KindOf(kind);
        return pieces.FirstOrDefault(p => p.Kind == ParagraphKinds.Image)
            ?? pieces.FirstOrDefault()
            ?? new FormattedParagraph(ParagraphKinds.HoldsText(storedKind) ? storedKind : ParagraphKinds.Text, string.Empty, null);
    }

    /// <summary>
    /// The length the chapter's limit counts (100,000): the words readers see (<see cref="FormattedParagraph.VisibleText"/>),
    /// so formatting doesn't take a writer's room.
    /// </summary>
    public static int VisibleLength(IEnumerable<FormattedParagraph> paragraphs) =>
        paragraphs.Sum(p => p.VisibleText.Length);

    /// <summary>
    /// The kind a <c>data-kind</c> attribute or a stored <c>ContentType</c> names (any case, spaces around); an unknown
    /// or missing one is <see cref="ParagraphKinds.Text"/>.
    /// </summary>
    public static string KindOf(string? value) => value?.Trim().ToLowerInvariant() switch
    {
        ParagraphKinds.Center => ParagraphKinds.Center,
        ParagraphKinds.Quote => ParagraphKinds.Quote,
        ParagraphKinds.Break => ParagraphKinds.Break,
        ParagraphKinds.Image => ParagraphKinds.Image,
        _ => ParagraphKinds.Text
    };

    /// <summary>
    /// A picture's address as stored: an absolute http or https URL, normalized and escaped (no spaces, quotes or
    /// angle brackets survive); null for any other scheme, a relative address, or none.
    /// </summary>
    public static string? ImageAddress(string? src)
    {
        if (string.IsNullOrWhiteSpace(src)
            || !Uri.TryCreate(src.Trim(), UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp)
            || string.IsNullOrEmpty(uri.Host))
        {
            return null;
        }

        return uri.AbsoluteUri;
    }

    /// <summary>Block elements: a paragraph ends where one begins or ends (besides <c>p</c>, <c>hr</c> and pictures).</summary>
    internal static readonly HashSet<string> BlockElements = new(StringComparer.OrdinalIgnoreCase)
    {
        "address", "article", "aside", "blockquote", "body", "caption", "center", "dd", "details", "dialog", "dir",
        "div", "dl", "dt", "fieldset", "figcaption", "figure", "footer", "form", "h1", "h2", "h3", "h4", "h5", "h6",
        "header", "hgroup", "html", "legend", "li", "listing", "main", "menu", "nav", "ol", "plaintext", "pre",
        "search", "section", "summary", "table", "tbody", "td", "tfoot", "th", "thead", "tr", "ul", "xmp"
    };

    /// <summary>Elements whose content isn't text a reader reads: removed with everything in them.</summary>
    internal static readonly HashSet<string> RemovedElements = new(StringComparer.OrdinalIgnoreCase)
    {
        "script", "style", "template", "noscript", "iframe", "frame", "frameset", "noframes", "object", "embed",
        "applet", "param", "svg", "math", "canvas", "video", "audio", "source", "track", "map", "area", "head",
        "title", "meta", "link", "base", "basefont", "bgsound", "input", "select", "option", "optgroup", "datalist",
        "textarea", "button", "keygen", "output", "progress", "meter", "slot"
    };

    // ---- Parsing: the HTML as a flat list of tokens ----

    [Flags]
    private enum Marks : byte
    {
        None = 0,
        Strong = 1,
        Em = 2,
        Underline = 4,
        Strike = 8
    }

    /// <summary>The inline marks in the order they are opened when several begin together.</summary>
    private static readonly Marks[] MarkOrder = [Marks.Strong, Marks.Em, Marks.Underline, Marks.Strike];

    private abstract record Token;

    /// <summary>Text without line breaks, with the inline marks around it.</summary>
    private sealed record TextToken(string Text, Marks Marks) : Token;

    /// <summary>A line break: a <c>&lt;br&gt;</c> (<paramref name="Tag"/>) or a line break in the text.</summary>
    private sealed record LineToken(bool Tag) : Token;

    /// <summary>A picture; <paramref name="Address"/> null when its address isn't one format v1 keeps.</summary>
    private sealed record ImageToken(string? Address) : Token;

    /// <summary>A block begins or ends; what follows is in a paragraph of <paramref name="Kind"/>.</summary>
    private sealed record BoundaryToken(string Kind) : Token;

    /// <summary>A scene break: <c>&lt;hr&gt;</c> or <c>&lt;p data-kind="break"&gt;</c>.</summary>
    private sealed record BreakToken : Token
    {
        public static readonly BreakToken Instance = new();
    }

    private static readonly LineToken TagLine = new(true);
    private static readonly LineToken TextLine = new(false);

    // One parser per thread: AngleSharp makes a new document for each parse, but its parser isn't documented as safe to
    // share between threads. The context element makes fragments parse as the content of a <body>, in no-quirks mode.
    [ThreadStatic] private static HtmlParser? parser;
    [ThreadStatic] private static IElement? body;

    private static List<Token> Tokenize(string html, string kind)
    {
        if (parser is null || body is null)
        {
            parser = new HtmlParser(new HtmlParserOptions { IsScripting = false });
            body = parser.ParseDocument("<!DOCTYPE html><html><body></body></html>").Body!;
        }

        var tokens = new List<Token>();
        foreach (var node in parser.ParseFragment(html, body))
        {
            Walk(node, Marks.None, kind, tokens);
        }

        return tokens;
    }

    private static void Walk(INode node, Marks marks, string kind, List<Token> tokens)
    {
        if (node is IText text)
        {
            AddText(text.Data, marks, tokens);
            return;
        }

        // Comments go, and foreign content (svg, math) with everything in it.
        if (node is not IElement element || element.NamespaceUri != NamespaceNames.HtmlUri)
        {
            return;
        }

        var name = element.LocalName;
        switch (name)
        {
            case "br":
                tokens.Add(TagLine);
                return;
            case "hr":
                tokens.Add(BreakToken.Instance);
                return;
            case "img":
                tokens.Add(new ImageToken(ImageAddress(element.GetAttribute("src"))));
                return;
            case "p":
                var own = KindOf(element.GetAttribute("data-kind"));
                if (own == ParagraphKinds.Break)
                {
                    tokens.Add(BreakToken.Instance);
                    return;
                }

                tokens.Add(new BoundaryToken(own));
                WalkChildren(element, marks, own, tokens);
                tokens.Add(new BoundaryToken(kind));
                return;
        }

        if (RemovedElements.Contains(name))
        {
            return;
        }

        if (BlockElements.Contains(name))
        {
            tokens.Add(new BoundaryToken(kind));
            WalkChildren(element, marks, kind, tokens);
            tokens.Add(new BoundaryToken(kind));
            return;
        }

        WalkChildren(element, marks | MarkOf(name), kind, tokens);
    }

    private static void WalkChildren(IElement element, Marks marks, string kind, List<Token> tokens)
    {
        foreach (var child in element.ChildNodes)
        {
            Walk(child, marks, kind, tokens);
        }
    }

    private static Marks MarkOf(string name) => name switch
    {
        "strong" or "b" => Marks.Strong,
        "em" or "i" => Marks.Em,
        "u" => Marks.Underline,
        "s" => Marks.Strike,
        _ => Marks.None
    };

    /// <summary>Text as text tokens, with a line token for each line break in it.</summary>
    private static void AddText(string data, Marks marks, List<Token> tokens)
    {
        var start = 0;
        for (var i = 0; i < data.Length; i++)
        {
            if (data[i] is not ('\n' or '\r'))
            {
                continue;
            }

            if (i > start)
            {
                tokens.Add(new TextToken(data[start..i], marks));
            }

            if (data[i] == '\r' && i + 1 < data.Length && data[i + 1] == '\n')
            {
                i++;
            }

            tokens.Add(TextLine);
            start = i + 1;
        }

        if (start < data.Length)
        {
            tokens.Add(new TextToken(data[start..], marks));
        }
    }

    // ---- Paragraphs out of the tokens ----

    private static List<FormattedParagraph> Paragraphs(List<Token> tokens, string kind)
    {
        var paragraphs = new List<FormattedParagraph>();
        var run = new List<Token>();
        foreach (var token in tokens)
        {
            switch (token)
            {
                case BoundaryToken boundary:
                    Flush(run, kind, paragraphs);
                    kind = boundary.Kind;
                    break;
                case BreakToken:
                    Flush(run, kind, paragraphs);
                    paragraphs.Add(FormattedParagraph.SceneBreak);
                    break;
                default:
                    run.Add(token);
                    break;
            }
        }

        Flush(run, kind, paragraphs);
        return paragraphs;
    }

    private static void Flush(List<Token> run, string kind, List<FormattedParagraph> paragraphs)
    {
        foreach (var part in SplitAtBlankLines(run))
        {
            Finish(part, kind, paragraphs);
        }

        run.Clear();
    }

    /// <summary>A blank line in the text (two line breaks with only spaces between) ends a paragraph.</summary>
    private static List<List<Token>> SplitAtBlankLines(List<Token> run)
    {
        var parts = new List<List<Token>> { new() };
        for (var i = 0; i < run.Count; i++)
        {
            if (run[i] == TextLine)
            {
                var next = i + 1;
                var lines = 1;
                while (next < run.Count && (run[next] == TextLine || run[next] is TextToken { Text: var blank } && IsCollapsible(blank)))
                {
                    if (run[next] == TextLine)
                    {
                        lines++;
                    }

                    next++;
                }

                if (lines > 1)
                {
                    parts.Add([]);
                    i = next - 1;
                    continue;
                }
            }

            parts[^1].Add(run[i]);
        }

        return parts;
    }

    /// <summary>One paragraph's worth of tokens: an image paragraph, or text paragraphs with any pictures split out.</summary>
    private static void Finish(List<Token> part, string kind, List<FormattedParagraph> paragraphs)
    {
        var addresses = part.OfType<ImageToken>().Select(p => p.Address).OfType<string>().ToList();
        if (addresses.Count == 1 && (kind == ParagraphKinds.Image || !HasWords(part)))
        {
            paragraphs.Add(FormattedParagraph.Picture(addresses[0], kind == ParagraphKinds.Image ? Caption(part) : null));
            return;
        }

        var textKind = kind == ParagraphKinds.Image ? ParagraphKinds.Text : kind;
        var piece = new List<Token>();
        foreach (var token in part)
        {
            switch (token)
            {
                case ImageToken { Address: { } address }:
                    AddText(piece, textKind, paragraphs);
                    paragraphs.Add(FormattedParagraph.Picture(address, null));
                    piece.Clear();
                    break;
                case ImageToken:
                    // A picture format v1 doesn't keep: it goes, and the text on either side stays together.
                    break;
                default:
                    piece.Add(token);
                    break;
            }
        }

        AddText(piece, textKind, paragraphs);
    }

    private static void AddText(List<Token> piece, string kind, List<FormattedParagraph> paragraphs)
    {
        var html = Serialize(piece);
        if (html.Length > 0)
        {
            paragraphs.Add(new FormattedParagraph(kind, html, null));
        }
    }

    private static bool HasWords(IEnumerable<Token> tokens) =>
        tokens.Any(t => t is TextToken text && text.Text.Any(c => !char.IsWhiteSpace(c)));

    /// <summary>A caption: the paragraph's text as plain text, on one line; null without words.</summary>
    private static string? Caption(IEnumerable<Token> tokens)
    {
        var text = new StringBuilder();
        foreach (var token in tokens)
        {
            switch (token)
            {
                case TextToken t:
                    text.Append(t.Text);
                    break;
                case LineToken:
                    text.Append(' ');
                    break;
            }
        }

        return PlainCaption(text.ToString());
    }

    /// <summary>A caption as stored: spaces and line breaks collapsed to single spaces, trimmed; null without words.</summary>
    private static string? PlainCaption(string? caption)
    {
        if (caption is null)
        {
            return null;
        }

        var text = new StringBuilder(caption.Length);
        var space = false;
        foreach (var c in caption)
        {
            if (IsCollapsible(c) || c is '\n' or '\r')
            {
                space = text.Length > 0;
                continue;
            }

            if (space)
            {
                text.Append(' ');
                space = false;
            }

            text.Append(c);
        }

        return ParagraphText.Words(text.ToString()).Length == 0 ? null : text.ToString();
    }

    /// <summary>Spaces HTML collapses (a no-break space is not one of them).</summary>
    private static bool IsCollapsible(char c) => c is ' ' or '\t' or '\f';

    private static bool IsCollapsible(string text) => text.All(IsCollapsible);

    // ---- Inline content as canonical HTML ----

    /// <summary>A run of text with its marks, or a line break (<see cref="Text"/> null).</summary>
    private sealed class Segment(StringBuilder? text, Marks marks)
    {
        public StringBuilder? Text { get; } = text;
        public Marks Marks { get; } = marks;
    }

    /// <summary>
    /// Inline tokens as canonical HTML: whitespace collapsed, none at the edges or around a line break, no line breaks
    /// at the edges; marks opened in one order and only where text needs them; text escaped. Empty when there are no
    /// words. Parsing the result and serializing it again gives the same string.
    /// </summary>
    private static string Serialize(IEnumerable<Token> tokens)
    {
        var segments = new List<Segment>();
        Marks? space = null; // a collapsed space waiting for the next character, with the marks it began in
        var lineStart = true;
        foreach (var token in tokens)
        {
            switch (token)
            {
                case TextToken text:
                    foreach (var c in text.Text)
                    {
                        if (IsCollapsible(c))
                        {
                            if (!lineStart)
                            {
                                space ??= text.Marks;
                            }

                            continue;
                        }

                        if (space is { } spaceMarks)
                        {
                            Append(segments, ' ', spaceMarks);
                            space = null;
                        }

                        Append(segments, c, text.Marks);
                        lineStart = false;
                    }

                    break;
                case LineToken:
                    space = null;
                    segments.Add(new Segment(null, Marks.None));
                    lineStart = true;
                    break;
            }
        }

        while (segments.Count > 0 && segments[^1].Text is null)
        {
            segments.RemoveAt(segments.Count - 1);
        }

        var first = segments.FindIndex(s => s.Text is not null);
        if (first < 0 || !segments.Any(s => s.Text?.ToString().Any(c => !char.IsWhiteSpace(c)) == true))
        {
            return string.Empty;
        }

        segments.RemoveRange(0, first);

        var html = new StringBuilder();
        var open = new List<Marks>(MarkOrder.Length);
        for (var i = 0; i < segments.Count; i++)
        {
            var segment = segments[i];
            // A line break keeps the marks of the text on both sides of it open, and only those.
            var marks = segment.Text is null ? MarksAround(segments, i) : segment.Marks;

            var kept = 0;
            while (kept < open.Count && (marks & open[kept]) != 0)
            {
                kept++;
            }

            for (var k = open.Count - 1; k >= kept; k--)
            {
                html.Append("</").Append(TagOf(open[k])).Append('>');
                open.RemoveAt(k);
            }

            foreach (var mark in MarkOrder)
            {
                if ((marks & mark) != 0 && !open.Contains(mark))
                {
                    html.Append('<').Append(TagOf(mark)).Append('>');
                    open.Add(mark);
                }
            }

            if (segment.Text is null)
            {
                html.Append("<br>");
            }
            else
            {
                Escape(segment.Text, html);
            }
        }

        for (var k = open.Count - 1; k >= 0; k--)
        {
            html.Append("</").Append(TagOf(open[k])).Append('>');
        }

        return html.ToString();
    }

    private static void Append(List<Segment> segments, char c, Marks marks)
    {
        if (segments.Count == 0 || segments[^1].Text is null || segments[^1].Marks != marks)
        {
            segments.Add(new Segment(new StringBuilder(), marks));
        }

        segments[^1].Text!.Append(c);
    }

    private static Marks MarksAround(List<Segment> segments, int index)
    {
        var before = Marks.None;
        for (var i = index - 1; i >= 0; i--)
        {
            if (segments[i].Text is not null)
            {
                before = segments[i].Marks;
                break;
            }
        }

        var after = Marks.None;
        for (var i = index + 1; i < segments.Count; i++)
        {
            if (segments[i].Text is not null)
            {
                after = segments[i].Marks;
                break;
            }
        }

        return before & after;
    }

    private static string TagOf(Marks mark) => mark switch
    {
        Marks.Strong => "strong",
        Marks.Em => "em",
        Marks.Underline => "u",
        _ => "s"
    };

    private static void Escape(StringBuilder text, StringBuilder html)
    {
        for (var i = 0; i < text.Length; i++)
        {
            switch (text[i])
            {
                case '&':
                    html.Append("&amp;");
                    break;
                case '<':
                    html.Append("&lt;");
                    break;
                case '>':
                    html.Append("&gt;");
                    break;
                case ' ':
                    html.Append("&nbsp;");
                    break;
                default:
                    html.Append(text[i]);
                    break;
            }
        }
    }
}

/// <summary>
/// A stored paragraph converted to format v1 (<see cref="ChapterFormat.Convert"/>): the paragraphs it becomes, the
/// pictures it held, and how many of them are kept (as image paragraphs; the others have no http(s) address).
/// </summary>
public sealed record StoredParagraphConversion(IReadOnlyList<FormattedParagraph> Paragraphs, int Pictures, int PicturesKept);
