using Application.Chapters.Paragraphs;
using Domain.Entities;

namespace Sareed_novels_backend.Tests.Unit;

/// <summary>
/// #77's word rule (<see cref="ChapterWords"/>): runs of the visible text between whitespace that have a letter or a
/// digit. Punctuation alone isn't a word, tashkeel and tatweel never split one, markup and character references are what
/// a reader sees, and a picture's paragraph has none.
/// </summary>
public class ChapterWordsTests
{
    [Theory]
    // Tashkeel inside words, punctuation stuck to them, and a dash and an ellipsis standing alone.
    [InlineData("قَالَ الرَّجُلُ: «مَرْحَبًا يَا صَدِيقِي!» — ثُمَّ مَضَى ...", 7)]
    // Every mark on one word: still one word.
    [InlineData("مُحَمَّدٌ", 1)]
    [InlineData("فَسَيَكْفِيكَهُمُ اللَّهُ", 2)]
    // Tatweel stretches a word; alone it isn't one.
    [InlineData("جمـــيل جدًا ـــ", 2)]
    // Arabic punctuation alone.
    [InlineData("، ؛ ؟ ! . ... « » — – - * * *", 0)]
    [InlineData("هل أتيت؟ نعم، أتيت.", 4)]
    // Digits are words, Arabic-Indic and Western alike, with what is stuck to them.
    [InlineData("عام ٢٠٢٦ أو 2026، بنسبة 15%", 6)]
    // Other scripts too, and words joined without a space are one.
    [InlineData("Hello, world! كلمة/كلمة well-known", 4)]
    // Whitespace of every kind separates; nothing at all is no words.
    [InlineData("كلمة أخرى\tوثالثة\nورابعة", 4)]
    [InlineData("", 0)]
    [InlineData("   \n\t ", 0)]
    [InlineData(null, 0)]
    public void Words_are_runs_between_whitespace_with_a_letter_or_digit(string? text, int words) =>
        Assert.Equal(words, ChapterWords.InText(text));

    [Theory]
    // The editor's markup is not words: inline tags inside a word don't split it, a line break does.
    [InlineData("<p class=\"min-h-[1em]\">كل<strong>مة</strong> <em>واحدة</em></p>", 2)]
    [InlineData("<p>سطر أول<br>سطر ثانٍ</p>", 4)]
    [InlineData("<p>كلمة&nbsp;وأخرى</p>", 2)]
    // A character reference a reader sees as punctuation alone, and an escaped tag a reader sees as text.
    [InlineData("<p>هذا &amp; ذاك</p>", 2)]
    [InlineData("<p>&lt;b&gt; كلمة</p>", 2)]
    // A picture inside a paragraph has no words.
    [InlineData("<p><img src=\"https://files.test/a.png\"> تعليق</p>", 1)]
    public void A_paragraph_counts_what_a_reader_sees(string html, int words) =>
        Assert.Equal(words, ChapterWords.Count([Paragraph(html)]));

    [Fact]
    public void A_chapter_is_the_sum_of_its_paragraphs_and_a_picture_or_a_scene_break_adds_nothing()
    {
        var paragraphs = new[]
        {
            Paragraph("<p>بِسْمِ اللَّهِ، نَبْدَأُ.</p>"),
            Paragraph("* * *"),
            Paragraph("https://files.test/novel-images/picture.png", "image"),
            Paragraph("سطر<br>وسطر آخر")
        };

        Assert.Equal(3 + 0 + 0 + 3, ChapterWords.Count(paragraphs));
        Assert.Equal(0, ChapterWords.Count([]));
    }

    private static ChapterParagraph Paragraph(string content, string contentType = "text") =>
        new() { Content = content, ContentType = contentType };
}
