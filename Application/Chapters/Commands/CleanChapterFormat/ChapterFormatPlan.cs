using Application.Chapters.Paragraphs;
using Domain.Constants;
using Domain.Entities;

namespace Application.Chapters.Commands.CleanChapterFormat;

/// <summary>
/// What the format maintenance does to one chapter's stored paragraphs, decided without touching the database: each
/// row converted with <see cref="ChapterFormat.Convert"/>, and kept as it is unless that conversion leaves every word a
/// reader sees, and every picture, where it was.
/// </summary>
public sealed class ChapterFormatPlan
{
    public enum Change
    {
        /// <summary>Already format v1.</summary>
        None,

        /// <summary>Stored again in format v1, same row.</summary>
        Rewrite,

        /// <summary>Only the stored hash didn't match the content.</summary>
        HashFix,

        /// <summary>Several paragraphs: the row keeps one, new rows hold the others.</summary>
        Split,

        /// <summary>Empty (no words, no picture) and no comment on it: removed.</summary>
        Remove,

        /// <summary>Left as it is (<see cref="Row.SkipReason"/>).</summary>
        Skip
    }

    public const string VisibleTextChanged = "VisibleTextChanged";
    public const string PictureDropped = "PictureDropped";
    public const string EmptyWithComments = "EmptyWithComments";

    /// <summary>
    /// A stored row and what happens to it. <see cref="Stored"/> is the row as it was read (<see cref="Apply"/> changes
    /// <see cref="Paragraph"/> in place).
    /// </summary>
    public sealed record Row(
        ChapterParagraph Paragraph,
        FormattedParagraph Stored,
        Change Change,
        StoredParagraphConversion Conversion,
        string? SkipReason,
        string WordsBefore,
        string WordsAfter);

    private ChapterFormatPlan(List<Row> rows) => this.rows = rows;

    private readonly List<Row> rows;

    public IReadOnlyList<Row> Rows => rows;

    /// <summary>Whether the plan changes anything in the chapter.</summary>
    public bool HasChanges => rows.Any(r => r.Change is not (Change.None or Change.Skip));

    /// <summary>
    /// The plan for a chapter's rows, in their order. Empty rows are planned for removal: call
    /// <see cref="KeepCommented"/> with those of <see cref="EmptyRows"/> that have comments.
    /// </summary>
    public static ChapterFormatPlan For(IEnumerable<ChapterParagraph> paragraphs) => new(paragraphs.Select(Analyze).ToList());

    /// <summary>The rows planned for removal as empty.</summary>
    public IEnumerable<Guid> EmptyRows => rows.Where(r => r.Change == Change.Remove).Select(r => r.Paragraph.Id);

    /// <summary>Empty rows that comments are on stay: removing a paragraph would delete its comments.</summary>
    public void KeepCommented(IReadOnlySet<Guid> commented)
    {
        for (var i = 0; i < rows.Count; i++)
        {
            if (rows[i].Change == Change.Remove && commented.Contains(rows[i].Paragraph.Id))
            {
                rows[i] = rows[i] with { Change = Change.Skip, SkipReason = EmptyWithComments };
            }
        }
    }

    private static Row Analyze(ChapterParagraph paragraph)
    {
        var conversion = ChapterFormat.Convert(paragraph.Content, paragraph.ContentType, paragraph.Caption);
        var pieces = conversion.Paragraphs;
        var before = WordsOf(paragraph);
        var after = string.Join(' ', pieces.Select(p => p.VisibleText).Where(t => t.Length > 0));

        var stored = new FormattedParagraph(paragraph.ContentType, paragraph.Content, paragraph.Caption);
        Row Planned(Change change, string? reason = null) => new(paragraph, stored, change, conversion, reason, before, after);

        // A picture has no words, so the words alone wouldn't tell that one is dropped.
        if (conversion.PicturesKept < conversion.Pictures)
        {
            return Planned(Change.Skip, PictureDropped);
        }

        if (before != after)
        {
            return Planned(Change.Skip, VisibleTextChanged);
        }

        if (pieces.Count == 0)
        {
            return Planned(Change.Remove);
        }

        if (pieces.Count > 1)
        {
            return Planned(Change.Split);
        }

        var piece = pieces[0];
        if (piece.Content != paragraph.Content || piece.Kind != paragraph.ContentType || piece.Caption != paragraph.Caption)
        {
            return Planned(Change.Rewrite);
        }

        return Planned(ParagraphText.Hash(piece.Content) == paragraph.ContentHash ? Change.None : Change.HashFix);
    }

    /// <summary>The words a reader sees in a stored paragraph: its visible text; an image's caption; nothing for a break.</summary>
    private static string WordsOf(ChapterParagraph paragraph) => ChapterFormat.KindOf(paragraph.ContentType) switch
    {
        ParagraphKinds.Break => string.Empty,
        ParagraphKinds.Image => ParagraphText.Words(paragraph.Caption),
        _ => ParagraphText.VisibleText(paragraph.Content)
    };

    /// <summary>
    /// Applies the plan to its rows (loaded, tracked, in the chapter's edit): the chapter after it, in order, and the rows
    /// it removes. A split row keeps its first part with words (or its first part), with its id and comments; the other
    /// parts are new rows created when it was. Rows move to their new places. The paragraphs' UpdatedAt is left: the
    /// author didn't change them.
    /// </summary>
    public (List<ChapterParagraph> Paragraphs, List<ChapterParagraph> Removed) Apply()
    {
        var paragraphs = new List<ChapterParagraph>(rows.Count);
        var removed = new List<ChapterParagraph>();
        foreach (var row in rows)
        {
            var paragraph = row.Paragraph;
            var pieces = row.Conversion.Paragraphs;
            switch (row.Change)
            {
                case Change.Rewrite or Change.HashFix:
                    ParagraphRows.Store(paragraph, pieces[0]);
                    paragraphs.Add(paragraph);
                    break;
                case Change.Split:
                    var keeper = Math.Max(0, pieces.ToList().FindIndex(p => ParagraphKinds.HoldsText(p.Kind)));
                    for (var i = 0; i < pieces.Count; i++)
                    {
                        if (i == keeper)
                        {
                            ParagraphRows.Store(paragraph, pieces[i]);
                            paragraphs.Add(paragraph);
                        }
                        else
                        {
                            paragraphs.Add(ParagraphRows.New(paragraph.ChapterId, pieces[i], 0, paragraph.CreatedAt));
                        }
                    }

                    break;
                case Change.Remove:
                    removed.Add(paragraph);
                    break;
                default:
                    paragraphs.Add(paragraph);
                    break;
            }
        }

        for (var i = 0; i < paragraphs.Count; i++)
        {
            if (paragraphs[i].OrderIndex != i)
            {
                paragraphs[i].OrderIndex = i;
            }
        }

        return (paragraphs, removed);
    }
}
