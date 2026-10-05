using Domain.Entities;

namespace Application.Chapters.Paragraphs;

/// <summary>Chapter paragraph rows holding format-v1 paragraphs (<see cref="ChapterFormat"/>).</summary>
public static class ParagraphRows
{
    /// <summary>A new row of the chapter holding <paramref name="paragraph"/>, at <paramref name="orderIndex"/>.</summary>
    public static ChapterParagraph New(Guid chapterId, FormattedParagraph paragraph, int orderIndex, DateTime createdAt) => new()
    {
        Id = Guid.NewGuid(),
        ChapterId = chapterId,
        Content = paragraph.Content,
        ContentHash = ParagraphText.Hash(paragraph.Content),
        ContentType = paragraph.Kind,
        Caption = paragraph.Caption,
        OrderIndex = orderIndex,
        CreatedAt = createdAt,
        CommentsCount = 0
    };

    /// <summary>
    /// Stores <paramref name="paragraph"/> in a row that keeps its id: its content, kind and caption, and the hash of
    /// that content. True when anything in the row changed.
    /// </summary>
    public static bool Store(ChapterParagraph row, FormattedParagraph paragraph)
    {
        var hash = ParagraphText.Hash(paragraph.Content);
        if (row.Content == paragraph.Content && row.ContentType == paragraph.Kind && row.Caption == paragraph.Caption
            && row.ContentHash == hash)
        {
            return false;
        }

        row.Content = paragraph.Content;
        row.ContentHash = hash;
        row.ContentType = paragraph.Kind;
        row.Caption = paragraph.Caption;
        return true;
    }

    /// <summary>The row as the API serves it (<see cref="ChapterFormat.Read"/>).</summary>
    public static FormattedParagraph Read(ChapterParagraph row) => ChapterFormat.Read(row.Content, row.ContentType, row.Caption);
}
