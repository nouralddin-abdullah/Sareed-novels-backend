using Domain.Constants;

namespace Domain.Entities;

public class ChapterParagraph
{
    public Guid Id { get; set; }
    public Guid ChapterId { get; set; }
    public Chapter Chapter { get; set; } = default!;

    /// <summary>
    /// The paragraph in chapter format v1 (#74): inline HTML (text, strong, em, u, s, br) for text, center and quote;
    /// <c>* * *</c> for a break; the picture's http(s) address for an image. Rows from before #74 may still hold the
    /// editor's raw HTML until the format maintenance converts them; what the API serves is cleaned either way.
    /// </summary>
    public string Content { get; set; } = default!;

    /// <summary>SHA-256 of <see cref="Content"/> as stored (ParagraphText.Hash); rewritten whenever the content is.</summary>
    public string ContentHash { get; set; } = default!;
    public int OrderIndex { get; set; }

    /// <summary>The paragraph's kind (<see cref="ParagraphKinds"/>).</summary>
    public string ContentType { get; set; } = ParagraphKinds.Text;

    /// <summary>An image paragraph's caption, plain text (not HTML); null for every other kind and for no caption.</summary>
    public string? Caption { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }
    public int CommentsCount { get; set; } = 0;
}
