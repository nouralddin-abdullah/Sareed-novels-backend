using Application.Common;

namespace Sareed_novels_backend.Tests.Unit;

public class PlainTextTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("<p><br></p>")]
    [InlineData("<p>&nbsp;</p>")]
    public void No_text_is_no_excerpt(string? html) => Assert.Null(PlainText.Excerpt(html, 140));

    [Fact]
    public void Tags_go_entities_are_decoded_and_whitespace_is_one_space()
    {
        var excerpt = PlainText.Excerpt("<p>قال <strong>الغريب</strong>:&nbsp;&quot;مرحباً&quot;<br>\n  ثم   مضى &amp; اختفى</p>", 140);

        Assert.Equal("قال الغريب: \"مرحباً\" ثم مضى & اختفى", excerpt);
    }

    [Fact]
    public void Formatting_inside_a_word_keeps_the_word_whole()
    {
        Assert.Equal("مستحيل تماماً", PlainText.Excerpt("<p>مست<strong>حيل</strong><BR/>تماماً</p>", 140));
    }

    [Fact]
    public void Long_text_is_cut_within_the_limit_and_marked()
    {
        var text = string.Join(" ", Enumerable.Repeat("كلمة", 60));

        var excerpt = PlainText.Excerpt($"<p>{text}</p>", 140)!;

        Assert.True(excerpt.Length <= 140);
        Assert.EndsWith(" كلمة…", excerpt); // on a whole word
        Assert.StartsWith(excerpt[..^1], text);
    }

    [Fact]
    public void A_word_that_ends_right_at_the_cut_is_kept()
    {
        // 9 + 1 + 9 characters: the cut after 19 falls between the second word and the third.
        var excerpt = PlainText.Excerpt("أبجدهوزحط كلمنسعفصق رشتثخذضظغ", 20);

        Assert.Equal("أبجدهوزحط كلمنسعفصق…", excerpt);
    }

    [Fact]
    public void Text_that_fits_is_kept_whole()
    {
        var text = new string('ا', 140);

        Assert.Equal(text, PlainText.Excerpt(text, 140));
    }

    [Fact]
    public void A_cut_never_splits_an_emoji_or_a_letter_from_its_tashkeel()
    {
        var emoji = PlainText.Excerpt(new string('ا', 138) + "😀😀😀", 140)!;
        var tashkeel = PlainText.Excerpt(string.Concat(Enumerable.Repeat("بِ", 10)), 10)!;

        Assert.Equal(new string('ا', 138) + "…", emoji);
        Assert.Equal("بِبِبِبِ…", tashkeel);
    }
}
