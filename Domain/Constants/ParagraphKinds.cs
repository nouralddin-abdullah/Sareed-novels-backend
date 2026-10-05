namespace Domain.Constants;

/// <summary>
/// The kinds of chapter paragraph in chapter format v1 (#74), stored in <c>ChapterParagraph.ContentType</c> and sent to
/// readers as <c>contentType</c>.
/// </summary>
public static class ParagraphKinds
{
    /// <summary>A normal paragraph (the default).</summary>
    public const string Text = "text";

    /// <summary>A centered paragraph: a poem, a title inside the chapter, a sign.</summary>
    public const string Center = "center";

    /// <summary>A set-off block: a letter, a message, a memory, a voice from elsewhere.</summary>
    public const string Quote = "quote";

    /// <summary>A scene break; its content is always <c>* * *</c>.</summary>
    public const string Break = "break";

    /// <summary>A picture: its content is one http(s) address, with an optional plain-text caption.</summary>
    public const string Image = "image";

    public static readonly IReadOnlyCollection<string> All = [Text, Center, Quote, Break, Image];

    /// <summary>Kinds whose content is inline text (words a reader reads), as opposed to a break or a picture.</summary>
    public static bool HoldsText(string kind) => kind is Text or Center or Quote;
}
