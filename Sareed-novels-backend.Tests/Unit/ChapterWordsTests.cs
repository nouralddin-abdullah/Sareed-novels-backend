using Application.Chapters.Paragraphs;
using Domain.Constants;
using Domain.Entities;

namespace Sareed_novels_backend.Tests.Unit;

/// <summary>
/// #77's word rule (<see cref="ChapterWords"/>): runs of the text a reader sees between whitespace that have a letter or
/// a digit. Punctuation alone isn't a word, tashkeel and tatweel never split one, markup and character references are
/// what a reader sees, an image's caption counts and a scene break doesn't, in chapter format v1 (#74) as sent and as
/// stored, paragraphs from before the format included.
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
    [InlineData("كلمة أخرى\tوثالثة\nورابعة", 4)]
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
    // Centered and set-off paragraphs are words like any other.
    [InlineData("<p data-kind=\"center\">بيت من الشعر</p><p data-kind=\"quote\">رسالة قصيرة</p>", 5)]
    // A picture has none, its caption has its own; a picture among text leaves the text's.
    [InlineData("<p><img src=\"https://files.test/a.png\"></p>", 0)]
    [InlineData("<p data-kind=\"image\"><img src=\"https://files.test/map.png\">خريطة المدينة</p>", 2)]
    [InlineData("<p>قبل <img src=\"https://files.test/a.png\"> بعد</p>", 2)]
    // A scene break stands for a separator, whatever was sent in it.
    [InlineData("<hr>", 0)]
    [InlineData("<p data-kind=\"break\">كلمات لا تظهر</p>", 0)]
    // Plain text with blank lines, as the API always took it.
    [InlineData("فقرة أولى\n\nفقرة ثانية طويلة", 5)]
    public void A_chapter_as_sent_counts_what_a_reader_sees(string content, int words) =>
        Assert.Equal(words, ChapterWords.Count(ChapterFormat.Parse(content)));

    [Fact]
    public void Stored_paragraphs_count_as_the_api_serves_them_those_from_before_the_format_included()
    {
        var rows = new[]
        {
            Row("بِسْمِ اللَّهِ، نَبْدَأُ.", ParagraphKinds.Text),
            Row("* * *", ParagraphKinds.Break),
            Row("https://files.test/novel-images/picture.png", ParagraphKinds.Image),
            Row("https://files.test/novel-images/map.png", ParagraphKinds.Image, caption: "خريطة المدينة"),
            // Stored before chapter format v1: the editor's markup, and a picture among the text.
            Row("<p class=\"min-h-[1em]\">سطر<br>وسطر آخر</p>", ParagraphKinds.Text),
            Row("<p>قبل <img src=\"https://files.test/a.png\"> بعد</p>", ParagraphKinds.Text)
        };

        Assert.Equal(3 + 0 + 0 + 2 + 3 + 2, ChapterWords.Count(rows));
        Assert.Equal(0, ChapterWords.Count(Array.Empty<ChapterParagraph>()));
        Assert.Equal(0, ChapterWords.Count(Array.Empty<FormattedParagraph>()));
    }

    [Fact]
    public void A_chapter_counts_the_same_as_sent_and_as_stored()
    {
        const string content = "<p>كان <strong>الليل</strong> طويلاً<br>والريح تعوي.</p><hr>" +
                               "<p data-kind=\"image\"><img src=\"https://files.test/map.png\">خريطة المدينة</p>" +
                               "<p data-kind=\"center\">النهاية</p>";
        var sent = ChapterFormat.Parse(content);
        var stored = sent.Select((paragraph, index) => ParagraphRows.New(Guid.NewGuid(), paragraph, index, DateTime.UtcNow));

        Assert.Equal(8, ChapterWords.Count(sent));
        Assert.Equal(8, ChapterWords.Count(stored));
    }

    private static ChapterParagraph Row(string content, string kind, string? caption = null) =>
        new() { Content = content, ContentType = kind, Caption = caption };
}
