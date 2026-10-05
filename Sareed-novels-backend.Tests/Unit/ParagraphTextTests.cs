using System.Security.Cryptography;
using System.Text;
using Application.Chapters.Paragraphs;

namespace Sareed_novels_backend.Tests.Unit;

/// <summary>What counts as a paragraph's text when deciding whether an edit changed it.</summary>
public class ParagraphTextTests
{
    [Theory]
    // The editor's paragraph wrapper, and a bare one
    [InlineData("<p class=\"min-h-[1em]\">كان ياما كان", "كان ياما كان")]
    [InlineData("<p>كان ياما كان</p>", "كان ياما كان")]
    // Inline formatting sits inside words or around them and adds no space
    [InlineData("كلمة <strong>مهمة</strong> جداً", "كلمة مهمة جداً")]
    [InlineData("مه<em>م</em>ة", "مهمة")]
    [InlineData("<u>خط</u> <s>مشطوب</s> <a href=\"https://x.test/?a=1&amp;b=2\">رابط</a> <span style=\"color:red\">ملون</span>", "خط مشطوب رابط ملون")]
    // Line breaks in any spelling separate words like a space
    [InlineData("سطر<br>سطر", "سطر سطر")]
    [InlineData("سطر<br/>سطر<br />سطر<BR>سطر", "سطر سطر سطر سطر")]
    [InlineData(".مصدومين <br><br><br><br>وبعد يومين", ".مصدومين وبعد يومين")]
    // Whitespace: runs, tabs, line endings, non-breaking spaces, leading and trailing
    [InlineData("  كان \t ياما\r\n\r\nكان  ", "كان ياما كان")]
    [InlineData("كان ياما&nbsp;&nbsp;كان", "كان ياما كان")]
    // Character references are the characters they stand for; an escaped tag is text
    [InlineData("قال &quot;نعم&quot; &amp; ذهب", "قال \"نعم\" & ذهب")]
    [InlineData("&#1603;&#x062A;&#1576;", "كتب")]
    [InlineData("&lt;b&gt;ليس وسماً&lt;/b&gt;", "<b>ليس وسماً</b>")]
    // Plain-text comparisons are not tags
    [InlineData("س < ص و ص > ع", "س < ص و ص > ع")]
    [InlineData("", "")]
    [InlineData("<p class=\"min-h-[1em]\"><br>", "")]
    // A picture or any block that ends a paragraph in chapter format v1 separates words too
    [InlineData("قبل<img src=\"https://x.test/a.png\">بعد", "قبل بعد")]
    [InlineData("قبل<section>بعد</section><dd>ثم</dd>", "قبل بعد ثم")]
    public void Visible_text_is_the_words_a_reader_sees(string content, string expected)
    {
        Assert.Equal(expected, ParagraphText.VisibleText(content));
    }

    [Fact]
    public void Visible_text_of_nothing_is_empty()
    {
        Assert.Equal("", ParagraphText.VisibleText(null));
    }

    [Theory]
    // One letter
    [InlineData("لن يكون مجر كتاب", "لن يكون مجرد كتاب")]
    // One diacritic (tashkeel), and a stray one removed
    [InlineData("ظهر الكتاب", "ظهر الكتابِ")]
    [InlineData("كان الكسندرٍ، صاحب", "كان الكسندر، صاحب")]
    // Hamza forms, taa marbuta and alef maqsura are different letters here, unlike in search
    [InlineData("الى المدينة", "إلى المدينه")]
    // Tatweel, punctuation, digits
    [InlineData("كتاب", "كتـــاب")]
    [InlineData("نعم", "نعم.")]
    [InlineData("بعد 8 أشهر", "بعد ٨ أشهر")]
    // Two words joined into one
    [InlineData("سطر<br>سطر", "سطرسطر")]
    public void Any_change_to_the_words_counts(string before, string after)
    {
        Assert.NotEqual(ParagraphText.VisibleText(before), ParagraphText.VisibleText(after));
    }

    [Fact]
    public void The_stored_hash_is_unchanged_sha256_of_the_trimmed_html()
    {
        static string Sha(string s) => Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(s)));

        Assert.Equal(Sha("<p class=\"min-h-[1em]\">نص"), ParagraphText.Hash("<p class=\"min-h-[1em]\">نص"));
        Assert.Equal(Sha("أ\nب ج"), ParagraphText.Hash("  أ\r\nب\tج \n"));
    }
}
