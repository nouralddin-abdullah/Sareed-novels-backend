using System.Text;
using Domain.Entities;

namespace Application.Chapters.Paragraphs;

/// <summary>
/// How many words a chapter has (#77): <see cref="Chapter.WordsCount"/> and a novel's total. The words are those a reader
/// sees (<see cref="FormattedParagraph.VisibleText"/>, chapter format v1, #74): a text, center or quote paragraph's text
/// without its markup, an image's caption (text the author wrote and readers see), nothing for a scene break. A word is
/// a run of that text between whitespace with at least one letter or digit, in any script. So punctuation alone («—»,
/// «...», «؟») isn't a word, while a word with punctuation attached («مرحبًا!») is one; tashkeel and tatweel are marks
/// inside a word, never splitting one, and tatweel alone isn't one. A chapter is counted as the API serves it, so
/// creating, saving and the backfill of stored chapters count alike, whatever format a stored paragraph is still in.
/// </summary>
public static class ChapterWords
{
    /// <summary>ـ (U+0640): Unicode files it as a letter, but it only stretches the letters around it.</summary>
    private const int Tatweel = 0x0640;

    /// <summary>The words of a chapter's paragraphs in chapter format v1 (as <see cref="ChapterFormat.Parse"/> makes them).</summary>
    public static int Count(IEnumerable<FormattedParagraph> paragraphs) => paragraphs.Sum(p => InText(p.VisibleText));

    /// <summary>The words of a chapter's stored paragraph rows, read as the API serves them (<see cref="ParagraphRows.Read"/>).</summary>
    public static int Count(IEnumerable<ChapterParagraph> rows) => Count(rows.Select(ParagraphRows.Read));

    /// <summary>The words of a visible text: runs between whitespace with at least one letter or digit.</summary>
    public static int InText(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return 0;
        }

        var words = 0;
        var counted = false; // the current run already has a letter or digit
        foreach (var rune in text.EnumerateRunes())
        {
            if (Rune.IsWhiteSpace(rune))
            {
                counted = false;
            }
            else if (!counted && Rune.IsLetterOrDigit(rune) && rune.Value != Tatweel)
            {
                counted = true;
                words++;
            }
        }
        return words;
    }
}
