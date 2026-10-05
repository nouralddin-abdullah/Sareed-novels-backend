using Domain.Constants;

namespace Application.Chapters.Paragraphs;

/// <summary>
/// One paragraph in chapter format v1 (#74), as <see cref="ChapterFormat"/> makes it: its kind
/// (<see cref="ParagraphKinds"/>) and content. For text, center and quote the content is inline HTML (text, strong, em,
/// u, s, br; text HTML-escaped); for a break it is <see cref="ChapterFormat.BreakContent"/>; for an image it is the
/// picture's http(s) address, with an optional plain-text <see cref="Caption"/>.
/// </summary>
public sealed record FormattedParagraph(string Kind, string Content, string? Caption)
{
    public static readonly FormattedParagraph SceneBreak = new(ParagraphKinds.Break, ChapterFormat.BreakContent, null);

    public static FormattedParagraph Picture(string address, string? caption) => new(ParagraphKinds.Image, address, caption);

    /// <summary>
    /// The words a reader sees in it (<see cref="ParagraphText.VisibleText"/>): an image's caption; nothing for a
    /// break, whose <c>* * *</c> only stands in for the separator.
    /// </summary>
    public string VisibleText => Kind switch
    {
        ParagraphKinds.Break => string.Empty,
        ParagraphKinds.Image => ParagraphText.Words(Caption),
        _ => ParagraphText.VisibleText(Content)
    };

    /// <summary>
    /// What <see cref="ParagraphMatcher"/> compares to tell which saved paragraph an edited one continues (#15): the
    /// words of text, center and quote alike (a paragraph's kind and its inline formatting are not part of its words);
    /// a break as a break; a picture by its address and caption.
    /// </summary>
    public string MatchKey => Kind switch
    {
        ParagraphKinds.Break => "b",
        ParagraphKinds.Image => "i" + Content + "\n" + VisibleText,
        _ => "t" + VisibleText
    };
}
