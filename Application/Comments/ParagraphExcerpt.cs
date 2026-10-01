using Application.Common;

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

    /// <summary>The excerpt of a paragraph's HTML (as the chapter editor saves it); null when it has no text.</summary>
    public static string? Of(string paragraphHtml) => PlainText.Excerpt(paragraphHtml, MaxLength);
}
