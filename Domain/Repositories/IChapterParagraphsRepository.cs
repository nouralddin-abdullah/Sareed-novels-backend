using Domain.Entities;

namespace Domain.Repositories;

public interface IChapterParagraphsRepository
{
    Task<List<ChapterParagraph>> GetChapterParagraphs(Guid chapterId);
    Task<ChapterParagraph?> GetParagraphById(Guid paragraphId);

    /// <summary>
    /// Saves an edit of a chapter's paragraphs in one transaction: <paramref name="paragraphs"/> is the chapter after
    /// the edit, paragraphs it keeps (as loaded by <see cref="GetChapterParagraphs"/>, with content and order updated
    /// in place) and new ones; <paramref name="removed"/> are loaded paragraphs it no longer has. Removed paragraphs
    /// go with their comments, those comments' replies, likes and notifications; the chapter's and the comment
    /// authors' comment counters drop to match.
    /// </summary>
    Task<RemovedParagraphComments> SaveEditedParagraphs(
        Guid chapterId, IReadOnlyList<ChapterParagraph> paragraphs, IReadOnlyList<ChapterParagraph> removed);
}

/// <summary>
/// Comments deleted with the paragraphs an edit removed, replies included: <see cref="Comments"/> rows in all,
/// <see cref="Visible"/> of them not already deleted by their authors.
/// </summary>
public sealed record RemovedParagraphComments(int Comments, int Visible)
{
    public static readonly RemovedParagraphComments None = new(0, 0);
}
