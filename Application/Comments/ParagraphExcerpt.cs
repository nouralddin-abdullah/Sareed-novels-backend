using System.Net;
using Application.Chapters.Paragraphs;
using Application.Common;
using Domain.Constants;

namespace Application.Comments;

/// <summary>
/// The start of a paragraph, quoted where a comment on it is shown out of the reader: the comment context
/// (GET /api/notifications/comment/{id}) and a member's comment list (#60). Plain text, at most
/// <see cref="MaxLength"/> characters. It is chapter text, so it is only for a viewer the reader would show the
/// chapter to (Chapters.ChapterAccess).
/// </summary>
internal static class ParagraphExcerpt
{
    public const int MaxLength = 140;

    /// <summary>
    /// The excerpt of a stored paragraph (its content, kind and caption) as the API serves it, in chapter format v1
    /// (#74): the words of text, center and quote; an image's caption; null for a break, or without words.
    /// </summary>
    public static string? Of(string content, string? kind, string? caption)
    {
        var paragraph = ChapterFormat.Read(content, kind, caption);
        return paragraph.Kind switch
        {
            ParagraphKinds.Break => null,
            ParagraphKinds.Image => PlainText.Excerpt(WebUtility.HtmlEncode(paragraph.Caption), MaxLength),
            _ => PlainText.Excerpt(paragraph.Content, MaxLength)
        };
    }
}
