using Domain.Entities;

namespace Domain.Repositories;

public interface IChapterParagraphsRepository
{
    Task<List<ChapterParagraph>> GetChapterParagraphs(Guid chapterId);
    Task<ChapterParagraph?> GetParagraphById(Guid paragraphId);

    /// <summary>
    /// Starts an edit of a chapter's text (the author's save, the format maintenance): a transaction that holds the
    /// chapter's text for this edit alone, so another edit of the same chapter waits until this one ends, with the
    /// chapter's paragraphs read inside it (<see cref="IChapterTextEdit.Paragraphs"/>). Nothing is written until
    /// <see cref="IChapterTextEdit.SaveAsync"/>; disposing the edit without saving rolls it back.
    /// </summary>
    Task<IChapterTextEdit> BeginEditAsync(Guid chapterId);

    /// <summary>The paragraphs of these chapters, by chapter id and in order, read without tracking them.</summary>
    Task<Dictionary<Guid, List<ChapterParagraph>>> GetParagraphsOfChaptersAsync(IReadOnlyCollection<Guid> chapterIds);

    /// <summary>Which of these paragraphs have comments: any, replies and comments their authors deleted included.</summary>
    Task<HashSet<Guid>> GetCommentedAsync(IReadOnlyCollection<Guid> paragraphIds);
}

/// <summary>An edit of one chapter's text in progress (<see cref="IChapterParagraphsRepository.BeginEditAsync"/>).</summary>
public interface IChapterTextEdit : IAsyncDisposable
{
    /// <summary>The chapter's paragraphs when the edit began, in order. Kept ones are changed in place.</summary>
    IReadOnlyList<ChapterParagraph> Paragraphs { get; }

    /// <summary>
    /// Which of these paragraphs have comments (as <see cref="IChapterParagraphsRepository.GetCommentedAsync"/>); until
    /// the edit ends, no comment can be added to those that have none.
    /// </summary>
    Task<HashSet<Guid>> GetCommentedAsync(IReadOnlyCollection<Guid> paragraphIds);

    /// <summary>
    /// Saves the edit and commits it: <paramref name="paragraphs"/> is the chapter after the edit, paragraphs it keeps
    /// (from <see cref="Paragraphs"/>, with content and order updated in place) and new ones; <paramref name="removed"/>
    /// are paragraphs it no longer has. Removed paragraphs go with their comments, those comments' replies, likes and
    /// notifications; the chapter's and the comment authors' comment counters drop to match, and the chapter's
    /// ParagraphsCount becomes the new count.
    /// </summary>
    Task<RemovedParagraphComments> SaveAsync(IReadOnlyList<ChapterParagraph> paragraphs, IReadOnlyList<ChapterParagraph> removed);
}

/// <summary>
/// Comments deleted with the paragraphs an edit removed, replies included: <see cref="Comments"/> rows in all,
/// <see cref="Visible"/> of them not already deleted by their authors.
/// </summary>
public sealed record RemovedParagraphComments(int Comments, int Visible)
{
    public static readonly RemovedParagraphComments None = new(0, 0);
}
