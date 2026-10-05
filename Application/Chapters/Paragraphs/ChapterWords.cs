using System.Text;
using Domain.Entities;

namespace Application.Chapters.Paragraphs;

/// <summary>
/// How many words a chapter has (#77): <see cref="Chapter.WordsCount"/> and a novel's total. A word is a run of the
/// visible text (<see cref="ParagraphText.VisibleText"/>: no markup, character references decoded, a line break as a
/// space) between whitespace that has at least one letter or digit, in any script. So punctuation alone («—», «...»,
/// «؟», a scene break's «* * *») isn't a word, while a word with punctuation attached («مرحبًا!») is one; tashkeel and
/// tatweel are marks inside a word, never splitting one, and tatweel alone isn't one. Paragraphs are counted as stored,
/// so creating, saving and the backfill count alike: a picture's paragraph (its content is a URL) has no words.
/// </summary>
public static class ChapterWords
{
    /// <summary>The kind (<see cref="ChapterParagraph.ContentType"/>) of a paragraph that is a picture, its URL as content.</summary>
    private const string Picture = "image";

    /// <summary>ـ (U+0640): Unicode files it as a letter, but it only stretches the letters around it.</summary>
    private const int Tatweel = 0x0640;

    /// <summary>The words of a chapter's paragraphs as they are stored.</summary>
    public static int Count(IEnumerable<ChapterParagraph> paragraphs) =>
        paragraphs.Sum(p => p.ContentType == Picture ? 0 : InText(ParagraphText.VisibleText(p.Content)));

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
