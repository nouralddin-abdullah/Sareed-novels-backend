using MediatR;

namespace Application.Chapters.Commands.CleanChapterFormat;

/// <summary>
/// The format maintenance (#74, POST /api/admin/chapters/clean-format): runs chapter format v1's cleaning
/// (Paragraphs.ChapterFormat) over the stored paragraphs, chapter by chapter in id order from <see cref="After"/>.
/// <list type="bullet">
/// <item>A dry run unless <see cref="DryRun"/> is false: it changes nothing and reports what the real pass would do.</item>
/// <item>The real pass converts each chapter in its own transaction, under the chapter's text lock (an author's save of
/// that chapter waits for it, and it for the save), reading the paragraphs again inside it. It is idempotent: what it
/// converted is already format v1 the next time.</item>
/// <item>It never changes the words a reader sees: a paragraph whose visible text the cleaning would change, or whose
/// picture it would drop (no http(s) address), is left as it is and reported. An empty paragraph is removed only when
/// no comment is on it.</item>
/// <item>One call works for about <see cref="CleanChapterFormatCommandHandler.TimeBudget"/>, then answers with
/// <c>nextCursor</c>: call again with <c>after</c> = that cursor until it is null.</item>
/// </list>
/// </summary>
public class CleanChapterFormatCommand : IRequest<ChapterFormatReport>
{
    public const int MaxBatchSize = 200;

    /// <summary>Report only (the default); false converts.</summary>
    public bool DryRun { get; init; } = true;

    /// <summary>Continue after this chapter id (the previous call's <c>nextCursor</c>); null starts from the first.</summary>
    public Guid? After { get; init; }

    /// <summary>Chapters read per query, 1 to <see cref="MaxBatchSize"/>.</summary>
    public int BatchSize { get; init; } = 50;
}

/// <summary>What the format maintenance did, or in a dry run would do, in the chapters this call went through.</summary>
public class ChapterFormatReport
{
    /// <summary>True: nothing was changed, and every count says what the real pass would do.</summary>
    public bool DryRun { get; set; }

    public int ChaptersChecked { get; set; }

    /// <summary>Chapters with at least one paragraph changed.</summary>
    public int ChaptersChanged { get; set; }

    public int ParagraphsChecked { get; set; }

    /// <summary>Already format v1: left as they are.</summary>
    public int ParagraphsUnchanged { get; set; }

    /// <summary>
    /// Paragraphs changed: <see cref="ParagraphsRewritten"/> + <see cref="ParagraphsSplit"/> +
    /// <see cref="EmptyParagraphsRemoved"/> + <see cref="HashesFixed"/>.
    /// </summary>
    public int ParagraphsChanged => ParagraphsRewritten + ParagraphsSplit + EmptyParagraphsRemoved + HashesFixed;

    /// <summary>Stored again in format v1 (markup, kind, caption, the picture's address), same id and comments.</summary>
    public int ParagraphsRewritten { get; set; }

    /// <summary>
    /// Made several paragraphs: a picture among its text is split out into an image paragraph, or blocks inside it become
    /// paragraphs. The stored paragraph, with its id and comments, keeps the first part with words.
    /// </summary>
    public int ParagraphsSplit { get; set; }

    /// <summary>New paragraphs the splits add.</summary>
    public int ParagraphsAdded { get; set; }

    /// <summary>Paragraphs without words or picture, and without comments: removed, as empty paragraphs are.</summary>
    public int EmptyParagraphsRemoved { get; set; }

    /// <summary>Content already format v1, but its stored ContentHash didn't match it: the hash is rewritten.</summary>
    public int HashesFixed { get; set; }

    /// <summary>Left as they are: see <see cref="Skipped"/> for the reasons.</summary>
    public int ParagraphsSkipped { get; set; }

    public PictureReport Pictures { get; set; } = new();

    /// <summary>The legacy <c>Chapters.Content</c> column, which nothing reads: reported, never changed here.</summary>
    public LegacyChapterContentReport LegacyChapterContent { get; set; } = new();

    /// <summary>A few paragraphs before and after, up to <see cref="ChapterFormatReportLimits.ExamplesPerChange"/> of each change.</summary>
    public List<FormatExample> Examples { get; set; } = [];

    /// <summary>The paragraphs left alone, up to <see cref="ChapterFormatReportLimits.Skipped"/>, with why.</summary>
    public List<SkippedParagraph> Skipped { get; set; } = [];

    /// <summary>Chapters whose conversion failed (the real pass), with the error: run the pass again for them.</summary>
    public List<ChapterFailure> Failures { get; set; } = [];

    /// <summary>Pass as <c>after</c> to continue; null when this call reached the last chapter.</summary>
    public Guid? NextCursor { get; set; }
}

public static class ChapterFormatReportLimits
{
    public const int ExamplesPerChange = 3;
    public const int Skipped = 50;
    public const int PictureExamples = 20;

    /// <summary>Content longer than this is cut in the report.</summary>
    public const int ContentLength = 500;
}

/// <summary>Stored paragraphs holding pictures (an &lt;img&gt;, or a stored image paragraph), so none goes unseen.</summary>
public class PictureReport
{
    /// <summary>Paragraphs holding at least one picture.</summary>
    public int Paragraphs { get; set; }

    /// <summary>Pictures with an http(s) address: kept, as image paragraphs.</summary>
    public int Kept { get; set; }

    /// <summary>Pictures without one, which format v1 doesn't keep: their paragraphs are skipped (left as they are).</summary>
    public int NotKept { get; set; }

    /// <summary>The paragraphs holding pictures, up to <see cref="ChapterFormatReportLimits.PictureExamples"/>.</summary>
    public List<PictureParagraph> Examples { get; set; } = [];
}

public class PictureParagraph
{
    public Guid ChapterId { get; set; }
    public Guid ParagraphId { get; set; }
    public int Pictures { get; set; }
    public int Kept { get; set; }
    public string Content { get; set; } = default!;
}

public class LegacyChapterContentReport
{
    /// <summary>Chapters with text in <c>Chapters.Content</c>.</summary>
    public int Chapters { get; set; }

    /// <summary>Of those, chapters without paragraphs: that column is their only text, and readers see none of it.</summary>
    public int WithoutParagraphs { get; set; }
}

/// <summary>A stored paragraph as it is, and what format v1 makes of it.</summary>
public class FormatExample
{
    public Guid ChapterId { get; set; }
    public Guid ParagraphId { get; set; }

    /// <summary>rewritten, split, emptyRemoved or hashFixed.</summary>
    public string Change { get; set; } = default!;

    public FormatExampleParagraph Before { get; set; } = default!;
    public List<FormatExampleParagraph> After { get; set; } = [];
}

public class FormatExampleParagraph
{
    public string Kind { get; set; } = default!;
    public string Content { get; set; } = default!;
    public string? Caption { get; set; }
}

/// <summary>A paragraph the maintenance leaves as it is.</summary>
public class SkippedParagraph
{
    public Guid ChapterId { get; set; }
    public Guid ParagraphId { get; set; }

    /// <summary>
    /// VisibleTextChanged: the cleaning would change the words a reader sees (<see cref="WordsBefore"/> and
    /// <see cref="WordsAfter"/>); PictureDropped: a picture without an http(s) address would go; EmptyWithComments: an
    /// empty paragraph that comments are on.
    /// </summary>
    public string Reason { get; set; } = default!;

    public FormatExampleParagraph Before { get; set; } = default!;
    public List<FormatExampleParagraph> After { get; set; } = [];
    public string WordsBefore { get; set; } = default!;
    public string WordsAfter { get; set; } = default!;
}

public class ChapterFailure
{
    public Guid ChapterId { get; set; }
    public string Error { get; set; } = default!;
}
